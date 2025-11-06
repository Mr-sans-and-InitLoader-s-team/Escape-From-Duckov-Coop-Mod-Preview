using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    public class RESTfulRequest
    {
        public string Endpoint;
        public string Method;
        public object Data;
        public long RequestId;
        public Action<RESTfulResponse> Callback;
        public float Timeout = 10f;
    }
    
    public class RESTfulResponse
    {
        public long RequestId;
        public int StatusCode;
        public string Data;
        public bool Success;
        public string Error;
        public Dictionary<string, string> Headers = new();
    }
    
    public class ResourceLink
    {
        public string Href { get; set; }
        public string Method { get; set; }
        public string Rel { get; set; }
    }
    
    public class RouteHandler
    {
        public string Method;
        public Regex Pattern;
        public Func<Dictionary<string, string>, string, RESTfulHttpResponse> Handler;
    }
    
    public class RESTfulHttpResponse
    {
        public int StatusCode = 200;
        public string Body = "";
        public Dictionary<string, string> Headers = new();
    }
    
    public class SimpleRESTfulTransport : MonoBehaviour
    {
        public static SimpleRESTfulTransport Instance { get; private set; }
        
        private string _baseUrl;
        private bool _isServer;
        private readonly Dictionary<long, RESTfulRequest> _pendingRequests = new();
        private long _nextRequestId = 1;
        
        private readonly List<RouteHandler> _routes = new();
        
        public bool IsInitialized { get; private set; }
        public string BaseUrl => _baseUrl;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        
        public void InitializeServer(int port)
        {
            _isServer = true;
            _baseUrl = $"http://localhost:{port}/";
            IsInitialized = true;
            
            Debug.Log($"[RESTfulTransport] RESTful server initialized (via game network)");
        }
        
        public void InitializeClient(string serverAddress, int port)
        {
            _isServer = false;
            _baseUrl = $"http://{serverAddress}:{port}/";
            IsInitialized = true;
            
            Debug.Log($"[RESTfulTransport] RESTful API client initialized, server: {_baseUrl}");
        }
        
        public void RegisterRoute(string method, string pattern, Func<Dictionary<string, string>, string, RESTfulHttpResponse> handler)
        {
            var regexPattern = Regex.Escape(pattern).Replace("\\{", "{").Replace("\\}", "}");
            regexPattern = Regex.Replace(regexPattern, @"\{(\w+)\}", @"(?<$1>[^/]+)");
            regexPattern = "^" + regexPattern + "$";
            
            _routes.Add(new RouteHandler
            {
                Method = method.ToUpper(),
                Pattern = new Regex(regexPattern),
                Handler = handler
            });
            
            Debug.Log($"[RESTfulTransport] Registered route: {method} {pattern}");
        }
        
    public void SendRequest(string endpoint, string method, object data, Action<RESTfulResponse> callback, float timeout = 10f)
    {
        if (!IsInitialized)
        {
            Debug.LogError("[RESTfulTransport] Not initialized");
            callback?.Invoke(new RESTfulResponse { Success = false, Error = "Not initialized", StatusCode = 0 });
            return;
        }
        
        var requestId = _nextRequestId++;
        
        if (_isServer)
        {
            string jsonData = data != null ? JsonConvert.SerializeObject(data) : "";
            var httpResponse = MatchRoute(method.ToUpper(), endpoint, jsonData);
            
            var response = new RESTfulResponse
            {
                RequestId = requestId,
                StatusCode = httpResponse.StatusCode,
                Success = httpResponse.StatusCode >= 200 && httpResponse.StatusCode < 300,
                Data = httpResponse.Body,
                Headers = httpResponse.Headers
            };
            
            callback?.Invoke(response);
            Debug.Log($"[RESTfulTransport] Server local call: {method} {endpoint} -> {httpResponse.StatusCode}");
            return;
        }
        
        var request = new RESTfulRequest
        {
            Endpoint = endpoint,
            Method = method.ToUpper(),
            Data = data,
            RequestId = requestId,
            Callback = callback,
            Timeout = timeout
        };
        
        _pendingRequests[request.RequestId] = request;
        SendRequestViaGameNetwork(request);
    }
        
        private void SendRequestViaGameNetwork(RESTfulRequest request)
        {
            var transport = HybridTransport.Instance;
            if (transport == null || transport.UDPTransport == null)
            {
                Debug.LogError("[RESTfulTransport] No UDP transport available");
                var errorResponse = new RESTfulResponse
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Error = "No transport available",
                    StatusCode = 0
                };
                request.Callback?.Invoke(errorResponse);
                _pendingRequests.Remove(request.RequestId);
                return;
            }
            
            var requestData = new
            {
                RequestId = request.RequestId,
                Endpoint = request.Endpoint,
                Method = request.Method,
                Data = request.Data != null ? JsonConvert.SerializeObject(request.Data) : ""
            };
            
            string json = JsonConvert.SerializeObject(requestData);
            
            var writer = new LiteNetLib.Utils.NetDataWriter();
            writer.Put((byte)254);
            writer.Put(json);
            
            transport.Send(writer, LiteNetLib.DeliveryMethod.ReliableOrdered);
            
            Debug.Log($"[RESTfulTransport] Sent request via game network: {request.Method} {request.Endpoint}");
            
            StartCoroutine(WaitForResponseCoroutine(request));
        }
        
        private IEnumerator WaitForResponseCoroutine(RESTfulRequest request)
        {
            float startTime = Time.time;
            
            while (Time.time - startTime < request.Timeout)
            {
                if (!_pendingRequests.ContainsKey(request.RequestId))
                {
                    yield break;
                }
                yield return null;
            }
            
            if (_pendingRequests.TryGetValue(request.RequestId, out var pendingRequest))
            {
                var timeoutResponse = new RESTfulResponse
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Error = "Request timeout",
                    StatusCode = 0
                };
                pendingRequest.Callback?.Invoke(timeoutResponse);
                _pendingRequests.Remove(request.RequestId);
            }
        }
        
        public void OnReceiveRequest(string json)
        {
            try
            {
                var requestData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                long requestId = Convert.ToInt64(requestData["RequestId"]);
                string endpoint = requestData["Endpoint"].ToString();
                string method = requestData["Method"].ToString();
                string body = requestData["Data"].ToString();
                
                var httpResponse = MatchRoute(method, endpoint, body);
                
                var responseData = new
                {
                    RequestId = requestId,
                    StatusCode = httpResponse.StatusCode,
                    Body = httpResponse.Body,
                    Headers = httpResponse.Headers
                };
                
                string responseJson = JsonConvert.SerializeObject(responseData);
                
                var writer = new LiteNetLib.Utils.NetDataWriter();
                writer.Put((byte)253);
                writer.Put(responseJson);
                
                var transport = HybridTransport.Instance;
                transport?.SendToAll(writer, LiteNetLib.DeliveryMethod.ReliableOrdered);
                
                Debug.Log($"[RESTfulTransport] Sent response via game network: {method} {endpoint} -> {httpResponse.StatusCode}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RESTfulTransport] Error processing request: {e.Message}");
            }
        }
        
        public void OnReceiveResponse(string json)
        {
            try
            {
                var responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                long requestId = Convert.ToInt64(responseData["RequestId"]);
                
                if (_pendingRequests.TryGetValue(requestId, out var request))
                {
                    var response = new RESTfulResponse
                    {
                        RequestId = requestId,
                        StatusCode = Convert.ToInt32(responseData["StatusCode"]),
                        Data = responseData["Body"].ToString(),
                        Headers = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseData["Headers"].ToString())
                    };
                    
                    response.Success = response.StatusCode >= 200 && response.StatusCode < 300;
                    
                    request.Callback?.Invoke(response);
                    _pendingRequests.Remove(requestId);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RESTfulTransport] Error processing response: {e.Message}");
            }
        }
        
        private RESTfulHttpResponse MatchRoute(string method, string path, string body)
        {
            foreach (var route in _routes)
            {
                if (route.Method != method.ToUpper()) continue;
                
                var match = route.Pattern.Match(path);
                if (!match.Success) continue;
                
                var parameters = new Dictionary<string, string>();
                foreach (Group group in match.Groups)
                {
                    if (int.TryParse(group.Name, out _)) continue;
                    parameters[group.Name] = group.Value;
                }
                
                return route.Handler(parameters, body);
            }
            
            return new RESTfulHttpResponse
            {
                StatusCode = 404,
                Body = JsonConvert.SerializeObject(new { error = "Resource not found", path })
            };
        }
        
        public string ProcessRequest(string method, string path, string body)
        {
            if (!_isServer)
            {
                return JsonConvert.SerializeObject(new { error = "Not a server" });
            }
            
            var response = MatchRoute(method, path, body);
            return response.Body;
        }
        
        public void Shutdown()
        {
            IsInitialized = false;
            _pendingRequests.Clear();
            _routes.Clear();
            Debug.Log("[RESTfulTransport] Shutdown");
        }
    }
}


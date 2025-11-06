using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    [Serializable]
    public class VoteResource
    {
        public int VoteId;
        public int SceneId;
        public string Status;
        public int ReadyCount;
        public int TotalCount;
        public long CreatedAt;
        public Dictionary<string, ResourceLink> Links;
        
        public static VoteResource Create(Vote vote, string baseUrl)
        {
            return new VoteResource
            {
                VoteId = vote.Id,
                SceneId = vote.SceneId,
                Status = vote.GetStatus(),
                ReadyCount = vote.GetReadyCount(),
                TotalCount = vote.Participants.Count,
                CreatedAt = vote.CreatedTimestamp,
                Links = new Dictionary<string, ResourceLink>
                {
                    ["self"] = new ResourceLink 
                    { 
                        Href = $"{baseUrl}/api/votes/{vote.Id}", 
                        Method = "GET",
                        Rel = "self"
                    },
                    ["participants"] = new ResourceLink 
                    { 
                        Href = $"{baseUrl}/api/votes/{vote.Id}/participants", 
                        Method = "GET",
                        Rel = "participants"
                    },
                    ["cancel"] = new ResourceLink 
                    { 
                        Href = $"{baseUrl}/api/votes/{vote.Id}", 
                        Method = "DELETE",
                        Rel = "cancel"
                    }
                }
            };
        }
    }
    
    [Serializable]
    public class ParticipantResource
    {
        public string PlayerId;
        public bool IsReady;
        public long UpdatedAt;
        public Dictionary<string, ResourceLink> Links;
    }
    
    [Serializable]
    public class VoteCreateRequest
    {
        public int SceneId;
        public List<string> Participants;
        public string InitiatorId;
    }
    
    [Serializable]
    public class ParticipantUpdateRequest
    {
        public bool IsReady;
    }
    
    public class Vote
    {
        public int Id;
        public int SceneId;
        public Dictionary<string, bool> Participants = new();
        public long CreatedTimestamp;
        public string InitiatorId;
        
        public string GetStatus()
        {
            if (Participants.Values.All(r => r)) return "completed";
            if (Participants.Values.Any(r => r)) return "in-progress";
            return "pending";
        }
        
        public int GetReadyCount() => Participants.Values.Count(r => r);
    }
    
    public class RESTfulVoteSystem : MonoBehaviour
    {
        public static RESTfulVoteSystem Instance { get; private set; }
        
        public Dictionary<int, Vote> _votes = new();
        private int _nextVoteId = 1;
        
        public event Action<int, List<string>> OnVoteStarted;
        public event Action<string, bool, int> OnPlayerReady;
        public event Action<int> OnVoteCompleted;
        
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
        
        public void RegisterRESTfulRoutes()
        {
            var transport = SimpleRESTfulTransport.Instance;
            if (transport == null)
            {
                Debug.LogWarning("[RESTfulVoteSystem] Transport not found, routes not registered");
                return;
            }
            
            Debug.Log("[RESTfulVoteSystem] Registering routes with transport");
            
            transport.RegisterRoute("POST", "/api/votes", CreateVote);
            transport.RegisterRoute("GET", "/api/votes/{voteId}", GetVote);
            transport.RegisterRoute("DELETE", "/api/votes/{voteId}", DeleteVote);
            transport.RegisterRoute("GET", "/api/votes/{voteId}/participants", GetParticipants);
            transport.RegisterRoute("PATCH", "/api/votes/{voteId}/participants/{playerId}", UpdateParticipantStatus);
            
            Debug.Log("[RESTfulVoteSystem] RESTful routes registered");
        }
        
        private RESTfulHttpResponse CreateVote(Dictionary<string, string> parameters, string body)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<VoteCreateRequest>(body);
                
                if (request.Participants == null || request.Participants.Count == 0)
                {
                    return new RESTfulHttpResponse
                    {
                        StatusCode = 400,
                        Body = JsonConvert.SerializeObject(new { error = "Participants list cannot be empty" })
                    };
                }
                
                var vote = new Vote
                {
                    Id = _nextVoteId++,
                    SceneId = request.SceneId,
                    CreatedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    InitiatorId = request.InitiatorId
                };
                
                foreach (var participant in request.Participants)
                {
                    vote.Participants[participant] = false;
                }
                
                _votes[vote.Id] = vote;
                
                Debug.Log($"[RESTfulVoteSystem] Vote {vote.Id} created for scene {vote.SceneId}");
                OnVoteStarted?.Invoke(vote.SceneId, request.Participants);
                
                var resource = VoteResource.Create(vote, SimpleRESTfulTransport.Instance.BaseUrl);
                
                return new RESTfulHttpResponse
                {
                    StatusCode = 201,
                    Body = JsonConvert.SerializeObject(resource),
                    Headers = new Dictionary<string, string>
                    {
                        ["Location"] = $"{SimpleRESTfulTransport.Instance.BaseUrl}/api/votes/{vote.Id}"
                    }
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[RESTfulVoteSystem] Error creating vote: {e}");
                return new RESTfulHttpResponse
                {
                    StatusCode = 400,
                    Body = JsonConvert.SerializeObject(new { error = e.Message })
                };
            }
        }
        
        private RESTfulHttpResponse GetVote(Dictionary<string, string> parameters, string body)
        {
            if (!parameters.TryGetValue("voteId", out var voteIdStr) || !int.TryParse(voteIdStr, out var voteId))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 400,
                    Body = JsonConvert.SerializeObject(new { error = "Invalid vote ID" })
                };
            }
            
            if (!_votes.TryGetValue(voteId, out var vote))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 404,
                    Body = JsonConvert.SerializeObject(new { error = "Vote not found" })
                };
            }
            
            var resource = VoteResource.Create(vote, SimpleRESTfulTransport.Instance.BaseUrl);
            
            return new RESTfulHttpResponse
            {
                StatusCode = 200,
                Body = JsonConvert.SerializeObject(resource)
            };
        }
        
        private RESTfulHttpResponse DeleteVote(Dictionary<string, string> parameters, string body)
        {
            if (!parameters.TryGetValue("voteId", out var voteIdStr) || !int.TryParse(voteIdStr, out var voteId))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 400,
                    Body = JsonConvert.SerializeObject(new { error = "Invalid vote ID" })
                };
            }
            
            if (!_votes.ContainsKey(voteId))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 404,
                    Body = JsonConvert.SerializeObject(new { error = "Vote not found" })
                };
            }
            
            _votes.Remove(voteId);
            Debug.Log($"[RESTfulVoteSystem] Vote {voteId} deleted");
            
            return new RESTfulHttpResponse
            {
                StatusCode = 204,
                Body = ""
            };
        }
        
        private RESTfulHttpResponse GetParticipants(Dictionary<string, string> parameters, string body)
        {
            if (!parameters.TryGetValue("voteId", out var voteIdStr) || !int.TryParse(voteIdStr, out var voteId))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 400,
                    Body = JsonConvert.SerializeObject(new { error = "Invalid vote ID" })
                };
            }
            
            if (!_votes.TryGetValue(voteId, out var vote))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 404,
                    Body = JsonConvert.SerializeObject(new { error = "Vote not found" })
                };
            }
            
            var participants = vote.Participants.Select(p => new ParticipantResource
            {
                PlayerId = p.Key,
                IsReady = p.Value,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Links = new Dictionary<string, ResourceLink>
                {
                    ["self"] = new ResourceLink
                    {
                        Href = $"{SimpleRESTfulTransport.Instance.BaseUrl}/api/votes/{voteId}/participants/{p.Key}",
                        Method = "PATCH",
                        Rel = "update"
                    }
                }
            }).ToList();
            
            return new RESTfulHttpResponse
            {
                StatusCode = 200,
                Body = JsonConvert.SerializeObject(participants)
            };
        }
        
        private RESTfulHttpResponse UpdateParticipantStatus(Dictionary<string, string> parameters, string body)
        {
            if (!parameters.TryGetValue("voteId", out var voteIdStr) || !int.TryParse(voteIdStr, out var voteId))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 400,
                    Body = JsonConvert.SerializeObject(new { error = "Invalid vote ID" })
                };
            }
            
            if (!parameters.TryGetValue("playerId", out var playerId))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 400,
                    Body = JsonConvert.SerializeObject(new { error = "Invalid player ID" })
                };
            }
            
            if (!_votes.TryGetValue(voteId, out var vote))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 404,
                    Body = JsonConvert.SerializeObject(new { error = "Vote not found" })
                };
            }
            
            if (!vote.Participants.ContainsKey(playerId))
            {
                return new RESTfulHttpResponse
                {
                    StatusCode = 404,
                    Body = JsonConvert.SerializeObject(new { error = "Participant not found" })
                };
            }
            
            try
            {
                var request = JsonConvert.DeserializeObject<ParticipantUpdateRequest>(body);
                vote.Participants[playerId] = request.IsReady;
                
                Debug.Log($"[RESTfulVoteSystem] Player {playerId} set to {(request.IsReady ? "ready" : "not ready")} in vote {voteId}");
                OnPlayerReady?.Invoke(playerId, request.IsReady, vote.SceneId);
                
                CheckVoteCompletion(voteId);
                
                var participant = new ParticipantResource
                {
                    PlayerId = playerId,
                    IsReady = request.IsReady,
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Links = new Dictionary<string, ResourceLink>
                    {
                        ["vote"] = new ResourceLink
                        {
                            Href = $"{SimpleRESTfulTransport.Instance.BaseUrl}/api/votes/{voteId}",
                            Method = "GET",
                            Rel = "vote"
                        }
                    }
                };
                
                return new RESTfulHttpResponse
                {
                    StatusCode = 200,
                    Body = JsonConvert.SerializeObject(participant)
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[RESTfulVoteSystem] Error updating participant: {e}");
                return new RESTfulHttpResponse
                {
                    StatusCode = 400,
                    Body = JsonConvert.SerializeObject(new { error = e.Message })
                };
            }
        }
        
        private void CheckVoteCompletion(int voteId)
        {
            if (_votes.TryGetValue(voteId, out var vote))
            {
                bool allReady = vote.Participants.Values.All(ready => ready);
                
                if (allReady)
                {
                    Debug.Log($"[RESTfulVoteSystem] All players ready for vote {voteId} (scene {vote.SceneId})");
                    OnVoteCompleted?.Invoke(vote.SceneId);
                }
            }
        }
        
        public void TriggerPlayerReady(string playerId, bool isReady, int sceneId)
        {
            OnPlayerReady?.Invoke(playerId, isReady, sceneId);
        }
        
        public void TriggerVoteCompleted(int sceneId)
        {
            OnVoteCompleted?.Invoke(sceneId);
        }
        
        public void Client_CreateVote(int sceneId, List<string> participants, Action<VoteResource> onSuccess, Action<string> onError = null)
        {
            var request = new VoteCreateRequest
            {
                SceneId = sceneId,
                Participants = participants,
                InitiatorId = NetService.Instance?.localPlayerStatus?.EndPoint ?? "unknown"
            };
            
            SimpleRESTfulTransport.Instance?.SendRequest("/api/votes", "POST", request, (response) =>
            {
                if (response.Success && response.StatusCode == 201)
                {
                    var voteResource = JsonConvert.DeserializeObject<VoteResource>(response.Data);
                    Debug.Log($"[RESTfulVoteSystem] Vote created successfully, ID: {voteResource.VoteId}");
                    onSuccess?.Invoke(voteResource);
                }
                else
                {
                    Debug.LogWarning($"[RESTfulVoteSystem] Failed to create vote: {response.Error}");
                    onError?.Invoke(response.Error ?? "Unknown error");
                }
            });
        }
        
        public void Client_GetVote(int voteId, Action<VoteResource> onSuccess, Action<string> onError = null)
        {
            SimpleRESTfulTransport.Instance?.SendRequest($"/api/votes/{voteId}", "GET", null, (response) =>
            {
                if (response.Success && response.StatusCode == 200)
                {
                    var voteResource = JsonConvert.DeserializeObject<VoteResource>(response.Data);
                    onSuccess?.Invoke(voteResource);
                }
                else
                {
                    onError?.Invoke(response.Error ?? "Vote not found");
                }
            });
        }
        
        public void Client_UpdateParticipantStatus(int voteId, string playerId, bool isReady, Action<ParticipantResource> onSuccess, Action<string> onError = null)
        {
            var request = new ParticipantUpdateRequest { IsReady = isReady };
            
            SimpleRESTfulTransport.Instance?.SendRequest($"/api/votes/{voteId}/participants/{playerId}", "PATCH", request, (response) =>
            {
                if (response.Success && response.StatusCode == 200)
                {
                    var participantResource = JsonConvert.DeserializeObject<ParticipantResource>(response.Data);
                    Debug.Log($"[RESTfulVoteSystem] Participant {playerId} updated to {(isReady ? "ready" : "not ready")}");
                    onSuccess?.Invoke(participantResource);
                }
                else
                {
                    Debug.LogWarning($"[RESTfulVoteSystem] Failed to update participant: {response.Error}");
                    onError?.Invoke(response.Error ?? "Unknown error");
                }
            });
        }
        
        public void Client_GetParticipants(int voteId, Action<List<ParticipantResource>> onSuccess, Action<string> onError = null)
        {
            SimpleRESTfulTransport.Instance?.SendRequest($"/api/votes/{voteId}/participants", "GET", null, (response) =>
            {
                if (response.Success && response.StatusCode == 200)
                {
                    var participants = JsonConvert.DeserializeObject<List<ParticipantResource>>(response.Data);
                    onSuccess?.Invoke(participants);
                }
                else
                {
                    onError?.Invoke(response.Error ?? "Unknown error");
                }
            });
        }
        
        public void Client_DeleteVote(int voteId, Action onSuccess, Action<string> onError = null)
        {
            SimpleRESTfulTransport.Instance?.SendRequest($"/api/votes/{voteId}", "DELETE", null, (response) =>
            {
                if (response.Success && response.StatusCode == 204)
                {
                    Debug.Log($"[RESTfulVoteSystem] Vote {voteId} deleted");
                    onSuccess?.Invoke();
                }
                else
                {
                    onError?.Invoke(response.Error ?? "Unknown error");
                }
            });
        }
    }
}


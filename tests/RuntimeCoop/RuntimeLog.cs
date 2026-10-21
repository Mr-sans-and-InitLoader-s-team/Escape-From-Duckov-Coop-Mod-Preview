using Newtonsoft.Json;
using UnityEngine;

namespace CoopRuntimeTests;

internal sealed class RuntimeLog : ILogHandler
{
    private readonly ILogHandler _inner;
    private readonly string _path;
    internal RuntimeLog(ILogHandler inner,string path) { _inner=inner;_path=path; }
    public void LogFormat(LogType type,UnityEngine.Object context,string format,params object[] args)
    {
        if(type is LogType.Error or LogType.Assert)
            File.AppendAllText(_path,JsonConvert.SerializeObject(new {time=Time.realtimeSinceStartup,type=type.ToString(),
                message=string.Format(format,args),stack=Environment.StackTrace})+"\n");
        _inner.LogFormat(type,context,format,args);
    }
    public void LogException(Exception exception,UnityEngine.Object context)
    {
        File.AppendAllText(_path,JsonConvert.SerializeObject(new {time=Time.realtimeSinceStartup,type="Exception",message=exception.ToString()})+"\n");
        _inner.LogException(exception,context);
    }
}

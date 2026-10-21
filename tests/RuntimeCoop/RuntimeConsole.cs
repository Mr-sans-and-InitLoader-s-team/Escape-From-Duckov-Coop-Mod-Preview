using System.Text;
using Newtonsoft.Json;

namespace CoopRuntimeTests;

internal sealed class RuntimeConsole : TextWriter
{
    private readonly TextWriter _inner;
    private readonly string _path;
    private int _traces;
    internal RuntimeConsole(TextWriter inner,string path) { _inner=inner;_path=path; }
    public override Encoding Encoding=>_inner.Encoding;
    public override void Write(char value)=>_inner.Write(value);
    public override void Write(string value)=>_inner.Write(value);
    public override void WriteLine(string value)
    {
        if(value?.Contains("startIndex")==true && System.Threading.Interlocked.Increment(ref _traces)<=20)
        {
            try { File.AppendAllText(_path,JsonConvert.SerializeObject(new {utc=DateTime.UtcNow,message=value,stack=Environment.StackTrace})+"\n"); }
            catch(IOException) { }
        }
        _inner.WriteLine(value);
    }
}

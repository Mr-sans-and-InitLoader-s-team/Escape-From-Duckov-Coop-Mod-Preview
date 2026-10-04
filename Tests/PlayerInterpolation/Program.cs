using System.Reflection;
using EscapeFromDuckovCoopMod;

static class Program
{
    static Vector3 X(float value) => new(value, 0, 0);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void At(NetInterpolator interp, double time)
    {
        Time.unscaledTimeAsDouble = time;
        typeof(NetInterpolator).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(interp, null);
    }
    static (NetInterpolator Interp, Transform Root) Create()
    {
        Time.unscaledTimeAsDouble = 0;
        var interp = new NetInterpolator
        {
            interpolationBackTime = 0, bufferTimeMultiplier = 0, catchupSpeed = 0,
            useGlobalAiInterval = false, maxExtrapolate = .24f
        };
        var root = new Transform(); interp.Init(root, root);
        return (interp, root);
    }
    static void Main()
    {
        var (interp, root) = Create();
        interp.PushStatus(X(10), Quaternion.identity);
        At(interp, 0);
        Check(root.position.x == 10, "status did not seed initial pose");
        Console.WriteLine("PASS full status initializes a new remote actor");

        Time.unscaledTimeAsDouble = .1;
        interp.PushArrival(X(11), Quaternion.identity, X(10)); At(interp, .1);
        Time.unscaledTimeAsDouble = .2;
        interp.PushStatus(X(2), Quaternion.identity);
        Check(root.position.x == 11, "status wrote raw pose before interpolation");
        At(interp, .2);
        Check(Math.Abs(root.position.x - 12) < .001, "late full status rewound live movement");
        Console.WriteLine("PASS delayed full status cannot overwrite active position stream");

        Time.unscaledTimeAsDouble = 1.2;
        interp.PushStatus(X(20), Quaternion.identity, Vector3.zero); At(interp, 1.2);
        Check(root.position.x == 20, "status fallback failed after stream outage");
        Console.WriteLine("PASS full status recovers position after realtime stream outage");

        (interp, root) = Create();
        interp.PushArrival(X(0), Quaternion.identity, X(10));
        Time.unscaledTimeAsDouble = .1; interp.PushArrival(X(1), Quaternion.identity, X(10));
        At(interp, .33); var beforeExpiry = root.position.x;
        At(interp, .35); var atExpiry = root.position.x;
        At(interp, .8);
        Check(atExpiry >= beforeExpiry && Math.Abs(root.position.x - atExpiry) < .001 && atExpiry > 3, "prediction expired by jumping back to old packet position");
        Console.WriteLine("PASS missed packets hold predicted endpoint instead of snapping backwards");

        (interp, root) = Create();
        interp.PushArrival(X(5), Quaternion.identity, Vector3.zero); At(interp, .8);
        Check(root.position.x == 5, "stationary actor drifted");
        Console.WriteLine("PASS stationary target remains stationary");

        (interp, root) = Create();
        interp.PushArrival(X(0), Quaternion.identity, X(10));
        float cameraX = -1;
        var order = typeof(NetInterpolator).GetCustomAttribute<DefaultExecutionOrder>().Order;
        var lateUpdates = new (int Order, Action Run)[]
        {
            (0, () => cameraX = root.position.x), // GameCamera has default execution order.
            (order, () => At(interp, .1))
        };
        foreach (var callback in lateUpdates.OrderBy(item => item.Order)) callback.Run();
        Check(order < 0 && cameraX == root.position.x && cameraX > 0, "camera sampled preceding frame's position");
        Console.WriteLine("PASS configured LateUpdate order gives camera the current interpolated pose");
        Console.WriteLine("6 player interpolation regression scenarios passed (Unity scheduling/math test doubles).");
    }
}

using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pathfinding;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeMapSurvey
{
    internal static async UniTask<object> Run(JObject command)
    {
        if ((bool?)command["walk"] != true)
        {
            var points = new Dictionary<(int, int, int), Vector3>();
            var grid = (float?)command["grid"] ?? 80f;
            var nodes = 0;
            if (AstarPath.active?.data?.graphs != null)
                foreach (var graph in AstarPath.active.data.graphs)
                {
                    if (graph == null) continue;
                    graph.GetNodes(node =>
                    {
                        if (!node.Walkable) return;
                        nodes++;
                        var point = (Vector3)node.position;
                        var key = (Mathf.FloorToInt(point.x / grid), Mathf.FloorToInt(point.y / 4f), Mathf.FloorToInt(point.z / grid));
                        if (!points.ContainsKey(key)) points[key] = point;
                    });
                }
            var supported = points.Values.Where(p => RuntimePlacement.Ground(p, out _)).ToArray();
            return new { nodes, grid, navigationCandidates = points.Count,
                anchors = supported.Select(p => new[] { p.x, p.y, p.z }).ToArray() };
        }
        var placed = RuntimePlacement.Move(command);
        var character = CharacterMainControl.Main;
        var start = character.transform.position;
        var distance = 0f;
        var worstFrame = 0f;
        var samples = 0;
        try
        {
            character.SetRunInput(false);
            for (var direction = 0; direction < 8; direction++)
            {
                var angle = direction * Mathf.PI / 4f;
                var input = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var until = Time.unscaledTime + .18f;
                while (Time.unscaledTime < until)
                {
                    var previous = character.transform.position;
                    character.SetAimPoint(previous + input * 10f);
                    character.SetMoveInput(input);
                    await UniTask.NextFrame();
                    distance += Vector3.Distance(previous, character.transform.position);
                    worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                    samples++;
                }
            }
        }
        finally { if (character) character.SetMoveInput(Vector3.zero); }
        var end=character.transform.position;
        return new { placed, start, end, supportedEnd=RuntimePlacement.Ground(end,out _) && Mathf.Abs(end.y-start.y)<3f, distance, worstFrame, samples,
            cursor = Cursor.lockState.ToString(), black = Duckov.UI.BlackScreen.Instance && Duckov.UI.BlackScreen.Instance.gameObject.activeInHierarchy };
    }
}

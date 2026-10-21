namespace Duckov.Economy
{
    public struct Cost { public long money; public bool IsFree => money <= 0; }
}
public class CostTaker : UnityEngine.MonoBehaviour { public Duckov.Economy.Cost CurrentCost; }
public class ConstructionSite : UnityEngine.MonoBehaviour
{
    private Duckov.Economy.Cost cost;
    private bool wasBuilt;
    public int BuiltCalls;
    public bool Built => wasBuilt;
    public void SetOriginalCost(long money) => cost.money = money;
    private void OnBuilt(CostTaker taker) { wasBuilt = true; BuiltCalls++; }
}

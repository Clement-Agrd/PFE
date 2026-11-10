using UnityEngine;

public class EnumTargetPriority
{
    public enum TargetPriority
    {
        None,
        
        HighDefPhy,
        LowDefPhy,
        
        HighDefMag,
        LoweDefMag,
        
        Tank,
        squishy,
        
        All,
        FirstOnPath,
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class VRC3CVRCollisionTagConvertionConfig
{
    public static VRC3CVRCollisionTagConvertionConfig DefaultConfig => new VRC3CVRCollisionTagConvertionConfig
    {
        Head = Operation.ConvertAndKeep,
        Hand = Operation.ConvertAndKeep,
        HandL = Operation.ConvertAndKeep,
        HandR = Operation.ConvertAndKeep,
        Finger = Operation.ConvertAndKeep,
        FingerL = Operation.ConvertAndKeep,
        FingerR = Operation.ConvertAndKeep,
        FingerIndex = Operation.ConvertAndKeep,
        FingerIndexL = Operation.ConvertAndKeep,
        FingerIndexR = Operation.ConvertAndKeep,
        FingerMiddle = Operation.Keep,
        FingerMiddleL = Operation.Keep,
        FingerMiddleR = Operation.Keep,
        FingerRing = Operation.Keep,
        FingerRingL = Operation.Keep,
        FingerRingR = Operation.Keep,
        FingerLittle = Operation.Keep,
        FingerLittleL = Operation.Keep,
        FingerLittleR = Operation.Keep,
    };
    public enum Operation
    {
        Inherit,
        ConvertAndKeep,
        Convert,
        Keep,
        Delete,
    }
    [Tooltip("mouth")] public Operation Head;
    [Tooltip("index")] public Operation Hand;
    [Tooltip("index")] public Operation HandL;
    [Tooltip("index")] public Operation HandR;
    [Tooltip("index")] public Operation Finger;
    [Tooltip("index")] public Operation FingerL;
    [Tooltip("index")] public Operation FingerR;
    [Tooltip("index")] public Operation FingerIndex;
    [Tooltip("index")] public Operation FingerIndexL;
    [Tooltip("index")] public Operation FingerIndexR;
    [Tooltip("index")] public Operation FingerMiddle;
    [Tooltip("index")] public Operation FingerMiddleL;
    [Tooltip("index")] public Operation FingerMiddleR;
    [Tooltip("index")] public Operation FingerRing;
    [Tooltip("index")] public Operation FingerRingL;
    [Tooltip("index")] public Operation FingerRingR;
    [Tooltip("index")] public Operation FingerLittle;
    [Tooltip("index")] public Operation FingerLittleL;
    [Tooltip("index")] public Operation FingerLittleR;
    public Operation All
    {
        set
        {
            Head = value;
            Hands = value;
            AllFingers = value;
        }
    }
    public Operation Hands
    {
        set
        {
            Hand = value;
            HandL = value;
            HandR = value;
        }
    }
    public Operation AllFingers
    {
        set
        {
            Fingers = value;
            FingerIndexes = value;
            FingerMiddles = value;
            FingerRings = value;
            FingerLittles = value;
        }
    }
    public Operation Fingers
    {
        set
        {
            Finger = value;
            FingerL = value;
            FingerR = value;
        }
    }
    public Operation FingerIndexes
    {
        set
        {
            FingerIndex = value;
            FingerIndexL = value;
            FingerIndexR = value;
        }
    }
    public Operation FingerMiddles
    {
        set
        {
            FingerMiddle = value;
            FingerMiddleL = value;
            FingerMiddleR = value;
        }
    }
    public Operation FingerRings
    {
        set
        {
            FingerRing = value;
            FingerRingL = value;
            FingerRingR = value;
        }
    }
    public Operation FingerLittles
    {
        set
        {
            FingerLittle = value;
            FingerLittleL = value;
            FingerLittleR = value;
        }
    }

    public static VRC3CVRCollisionTagConvertionConfig WithInherits(IEnumerable<VRC3CVRCollisionTagConvertionConfig> parentsToChild)
    {
        var config = new VRC3CVRCollisionTagConvertionConfig();
        foreach (var c in parentsToChild)
        {
            config.Head = Is(config.Head, c.Head);
            config.Hand = Is(config.Hand, c.Hand);
            config.HandL = Is(config.HandL, c.HandL);
            config.HandR = Is(config.HandR, c.HandR);
            config.Finger = Is(config.Finger, c.Finger);
            config.FingerL = Is(config.FingerL, c.FingerL);
            config.FingerR = Is(config.FingerR, c.FingerR);
            config.FingerIndex = Is(config.FingerIndex, c.FingerIndex);
            config.FingerIndexL = Is(config.FingerIndexL, c.FingerIndexL);
            config.FingerIndexR = Is(config.FingerIndexR, c.FingerIndexR);
            config.FingerMiddle = Is(config.FingerMiddle, c.FingerMiddle);
            config.FingerMiddleL = Is(config.FingerMiddleL, c.FingerMiddleL);
            config.FingerMiddleR = Is(config.FingerMiddleR, c.FingerMiddleR);
            config.FingerRing = Is(config.FingerRing, c.FingerRing);
            config.FingerRingL = Is(config.FingerRingL, c.FingerRingL);
            config.FingerRingR = Is(config.FingerRingR, c.FingerRingR);
            config.FingerLittle = Is(config.FingerLittle, c.FingerLittle);
            config.FingerLittleL = Is(config.FingerLittleL, c.FingerLittleL);
            config.FingerLittleR = Is(config.FingerLittleR, c.FingerLittleR);
        }
        return config;
    }

    bool TryGetCollisionTagMapping(string collisionTag, out Operation op, out string cvrType)
    {
        // cf. https://discord.com/channels/410126604237406209/797279576459968555/1127093496923308103
        // https://discord.com/channels/410126604237406209/588350685255565344/1327758763242815539
        switch (collisionTag)
        {
            case "Head": op = Head; cvrType = "mouth"; return true;
            case "Hand": op = Hand; cvrType = "index"; return true;
            case "HandL": op = HandL; cvrType = "index"; return true;
            case "HandR": op = HandR; cvrType = "index"; return true;
            case "Finger": op = Finger; cvrType = "index"; return true;
            case "FingerL": op = FingerL; cvrType = "index"; return true;
            case "FingerR": op = FingerR; cvrType = "index"; return true;
            case "FingerIndex": op = FingerIndex; cvrType = "index"; return true;
            case "FingerIndexL": op = FingerIndexL; cvrType = "index"; return true;
            case "FingerIndexR": op = FingerIndexR; cvrType = "index"; return true;
            case "FingerMiddle": op = FingerMiddle; cvrType = "index"; return true;
            case "FingerMiddleL": op = FingerMiddleL; cvrType = "index"; return true;
            case "FingerMiddleR": op = FingerMiddleR; cvrType = "index"; return true;
            case "FingerRing": op = FingerRing; cvrType = "index"; return true;
            case "FingerRingL": op = FingerRingL; cvrType = "index"; return true;
            case "FingerRingR": op = FingerRingR; cvrType = "index"; return true;
            case "FingerLittle": op = FingerLittle; cvrType = "index"; return true;
            case "FingerLittleL": op = FingerLittleL; cvrType = "index"; return true;
            case "FingerLittleR": op = FingerLittleR; cvrType = "index"; return true;
            default: op = Operation.Inherit; cvrType = null; return false;
        }
    }

    public string[] CollisionTagToCVRType(string collisionTag)
    {
        if (!TryGetCollisionTagMapping(collisionTag, out var op, out var cvrType))
        {
            return new string[] { collisionTag };
        }
        switch (op)
        {
            case Operation.ConvertAndKeep: return new string[] { cvrType, collisionTag };
            case Operation.Convert: return new string[] { cvrType };
            case Operation.Keep: return new string[] { collisionTag };
            case Operation.Delete: return new string[0];
            default: return new string[] { collisionTag };
        }
    }

    static Operation Is(Operation baseOperation, Operation operation)
    {
        return operation == Operation.Inherit ? baseOperation : operation;
    }
}

using System;
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

    public Func<string, string[]> CollisionTagToCVRType(VRC3CVRCollisionTagConvertionConfig baseConfig) => (string collisionTag) => CollisionTagToCVRType(baseConfig, collisionTag);
    public string[] CollisionTagToCVRType(VRC3CVRCollisionTagConvertionConfig baseConfig, string collisionTag)
    {
        string cvrType = null;
        Operation op;
        // cf. https://discord.com/channels/410126604237406209/797279576459968555/1127093496923308103
        // https://discord.com/channels/410126604237406209/588350685255565344/1327758763242815539
        switch (collisionTag)
        {
            case "Head":
                op = Is(baseConfig.Head, Head);
                cvrType = "mouth";
                break;
            case "Hand":
                op = Is(baseConfig.Hand, Hand);
                cvrType = "index";
                break;
            case "HandL":
                op = Is(baseConfig.HandL, HandL);
                cvrType = "index";
                break;
            case "HandR":
                op = Is(baseConfig.HandR, HandR);
                cvrType = "index";
                break;
            case "Finger":
                op = Is(baseConfig.Finger, Finger);
                cvrType = "index";
                break;
            case "FingerL":
                op = Is(baseConfig.FingerL, FingerL);
                cvrType = "index";
                break;
            case "FingerR":
                op = Is(baseConfig.FingerR, FingerR);
                cvrType = "index";
                break;
            case "FingerIndex":
                op = Is(baseConfig.FingerIndex, FingerIndex);
                cvrType = "index";
                break;
            case "FingerIndexL":
                op = Is(baseConfig.FingerIndexL, FingerIndexL);
                cvrType = "index";
                break;
            case "FingerIndexR":
                op = Is(baseConfig.FingerIndexR, FingerIndexR);
                cvrType = "index";
                break;
            case "FingerMiddle":
                op = Is(baseConfig.FingerMiddle, FingerMiddle);
                cvrType = "index";
                break;
            case "FingerMiddleL":
                op = Is(baseConfig.FingerMiddleL, FingerMiddleL);
                cvrType = "index";
                break;
            case "FingerMiddleR":
                op = Is(baseConfig.FingerMiddleR, FingerMiddleR);
                cvrType = "index";
                break;
            case "FingerRing":
                op = Is(baseConfig.FingerRing, FingerRing);
                cvrType = "index";
                break;
            case "FingerRingL":
                op = Is(baseConfig.FingerRingL, FingerRingL);
                cvrType = "index";
                break;
            case "FingerRingR":
                op = Is(baseConfig.FingerRingR, FingerRingR);
                cvrType = "index";
                break;
            case "FingerLittle":
                op = Is(baseConfig.FingerLittle, FingerLittle);
                cvrType = "index";
                break;
            case "FingerLittleL":
                op = Is(baseConfig.FingerLittleL, FingerLittleL);
                cvrType = "index";
                break;
            case "FingerLittleR":
                op = Is(baseConfig.FingerLittleR, FingerLittleR);
                cvrType = "index";
                break;
            default:
                return new string[] { collisionTag };
        }
        if (op == Operation.ConvertAndKeep)
        {
            return new string[] { cvrType, collisionTag };
        }
        else if (op == Operation.Convert)
        {
            return new string[] { cvrType };
        }
        else if (op == Operation.Keep)
        {
            return new string[] { collisionTag };
        }
        else if (op == Operation.Delete)
        {
            return new string[0];
        }
        // Inherit
        return new string[] { collisionTag };
    }

    static Operation Is(Operation baseOperation, Operation operation)
    {
        return operation == Operation.Inherit ? baseOperation : operation;
    }
}

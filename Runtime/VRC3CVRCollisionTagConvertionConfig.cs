using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[Serializable]
public class VRC3CVRCollisionTagConvertionConfig
{
    public static VRC3CVRCollisionTagConvertionConfig DefaultConfig => new VRC3CVRCollisionTagConvertionConfig
    {
        Head = Operation.Convert,
        Torso = Operation.Convert,
        Hand = Operation.Convert,
        HandL = Operation.Convert,
        HandR = Operation.Convert,
        Foot = Operation.Convert,
        FootL = Operation.Convert,
        FootR = Operation.Convert,
        Finger = Operation.Convert,
        FingerL = Operation.Convert,
        FingerR = Operation.Convert,
        FingerIndex = Operation.Convert,
        FingerIndexL = Operation.Convert,
        FingerIndexR = Operation.Convert,
        FingerMiddle = Operation.Convert,
        FingerMiddleL = Operation.Convert,
        FingerMiddleR = Operation.Convert,
        FingerRing = Operation.Convert,
        FingerRingL = Operation.Convert,
        FingerRingR = Operation.Convert,
        FingerLittle = Operation.Convert,
        FingerLittleL = Operation.Convert,
        FingerLittleR = Operation.Convert,
    };
    public enum Operation
    {
        Inherit,
        ConvertAndKeep,
        Convert,
        Keep,
        Delete,
    }
    [Tooltip("Head")] public Operation Head;
    [Tooltip("Torso")] public Operation Torso;
    [Tooltip("Hand")] public Operation Hand;
    [Tooltip("LeftHand")] public Operation HandL;
    [Tooltip("RightHand")] public Operation HandR;
    [Tooltip("Foot")] public Operation Foot;
    [Tooltip("LeftFoot")] public Operation FootL;
    [Tooltip("RightFoot")] public Operation FootR;
    [Tooltip("Finger")] public Operation Finger;
    [Tooltip("Left(Fingers)")] public Operation FingerL;
    [Tooltip("Right(Fingers)")] public Operation FingerR;
    [Tooltip("Index")] public Operation FingerIndex;
    [Tooltip("LeftIndex")] public Operation FingerIndexL;
    [Tooltip("RightIndex")] public Operation FingerIndexR;
    [Tooltip("LeftMiddle + RightMiddle")] public Operation FingerMiddle;
    [Tooltip("LeftMiddle")] public Operation FingerMiddleL;
    [Tooltip("RightMiddle")] public Operation FingerMiddleR;
    [Tooltip("LeftRing + RightRing")] public Operation FingerRing;
    [Tooltip("LeftRing")] public Operation FingerRingL;
    [Tooltip("RightRing")] public Operation FingerRingR;
    [Tooltip("LeftLittle + RightLittle")] public Operation FingerLittle;
    [Tooltip("LeftLittle")] public Operation FingerLittleL;
    [Tooltip("RightLittle")] public Operation FingerLittleR;
    public Operation All
    {
        set
        {
            Head = value;
            Torso = value;
            Hands = value;
            Foots = value;
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
    public Operation Foots
    {
        set
        {
            Foot = value;
            FootL = value;
            FootR = value;
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
            config.Torso = Is(config.Torso, c.Torso);
            config.Hand = Is(config.Hand, c.Hand);
            config.HandL = Is(config.HandL, c.HandL);
            config.HandR = Is(config.HandR, c.HandR);
            config.Foot = Is(config.Foot, c.Foot);
            config.FootL = Is(config.FootL, c.FootL);
            config.FootR = Is(config.FootR, c.FootR);
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

    bool TryGetCollisionTagMapping(string collisionTag, out Operation op, out string[] cvrTypes)
    {
        // cf. https://discord.com/channels/410126604237406209/797279576459968555/1127093496923308103
        // https://discord.com/channels/410126604237406209/588350685255565344/1327758763242815539
        switch (collisionTag)
        {
            case "Head": op = Head; cvrTypes = new string[] { "Head" }; return true;
            case "Torso": op = Torso; cvrTypes = new string[] { "Torso" }; return true;
            case "Hand": op = Hand; cvrTypes = new string[] { "Hand" }; return true;
            case "HandL": op = HandL; cvrTypes = new string[] { "LeftHand" }; return true;
            case "HandR": op = HandR; cvrTypes = new string[] { "RightHand" }; return true;
            case "Foot": op = Foot; cvrTypes = new string[] { "Foot" }; return true;
            case "FootL": op = FootL; cvrTypes = new string[] { "LeftFoot" }; return true;
            case "FootR": op = FootR; cvrTypes = new string[] { "RightFoot" }; return true;
            case "Finger": op = Finger; cvrTypes = new string[] { "Finger" }; return true;
            case "FingerL": op = FingerL; cvrTypes = new string[] { "LeftIndex", "LeftMiddle", "LeftRing", "LeftLittle" }; return true;
            case "FingerR": op = FingerR; cvrTypes = new string[] { "RightIndex", "RightMiddle", "RightRing", "RightLittle" }; return true;
            case "FingerIndex": op = FingerIndex; cvrTypes = new string[] { "Index" }; return true;
            case "FingerIndexL": op = FingerIndexL; cvrTypes = new string[] { "LeftIndex" }; return true;
            case "FingerIndexR": op = FingerIndexR; cvrTypes = new string[] { "RightIndex" }; return true;
            case "FingerMiddle": op = FingerMiddle; cvrTypes = new string[] { "LeftMiddle", "RightMiddle" }; return true;
            case "FingerMiddleL": op = FingerMiddleL; cvrTypes = new string[] { "LeftMiddle" }; return true;
            case "FingerMiddleR": op = FingerMiddleR; cvrTypes = new string[] { "RightMiddle" }; return true;
            case "FingerRing": op = FingerRing; cvrTypes = new string[] { "LeftRing", "RightRing" }; return true;
            case "FingerRingL": op = FingerRingL; cvrTypes = new string[] { "LeftRing" }; return true;
            case "FingerRingR": op = FingerRingR; cvrTypes = new string[] { "RightRing" }; return true;
            case "FingerLittle": op = FingerLittle; cvrTypes = new string[] { "LeftLittle", "RightLittle" }; return true;
            case "FingerLittleL": op = FingerLittleL; cvrTypes = new string[] { "LeftLittle" }; return true;
            case "FingerLittleR": op = FingerLittleR; cvrTypes = new string[] { "RightLittle" }; return true;
            default: op = Operation.Inherit; cvrTypes = null; return false;
        }
    }

    public string[] CollisionTagToCVRType(string collisionTag)
    {
        if (!TryGetCollisionTagMapping(collisionTag, out var op, out var cvrTypes))
        {
            return new string[] { collisionTag };
        }
        switch (op)
        {
            case Operation.ConvertAndKeep: return cvrTypes.Concat(new string[] { collisionTag }).Distinct().ToArray();
            case Operation.Convert: return cvrTypes;
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

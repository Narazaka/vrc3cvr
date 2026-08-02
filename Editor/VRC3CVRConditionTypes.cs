#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor.Animations;
using UnityEngine;

// Unity refuses a transition whose condition mode does not match its parameter's type, and says so
// as "uses parameter 'X' which is not compatible with condition type" while leaving the layer dead.
// That happens whenever the avatar declared a parameter as a different type than the condition
// expects -- most often a Bool that had to be declared Float because a blend tree's blend parameter
// can only be a Float. CopyParametersTo keeps the first declaration it sees, so that type reaches
// the converted controller and every condition written against the other type breaks.
//
// ChilloutVR itself does not care: CVRAnimatorManager reads Animator.parameters for the real type
// and dispatches to SetFloat/SetInteger/SetBool accordingly, so a Float-declared IsLocal still
// receives its value. Only the conditions need rewriting.
public static class VRC3CVRConditionTypes
{
    // Rewrites the condition so it means the same thing against a parameter of the given type.
    // Returns false when no equivalent exists, in which case the caller should drop the condition
    // and warn -- keeping it would break the whole layer, not just that transition.
    public static bool TryAdapt(
        AnimatorCondition condition,
        AnimatorControllerParameterType parameterType,
        out AnimatorCondition adapted)
    {
        adapted = condition;

        switch (parameterType)
        {
            case AnimatorControllerParameterType.Bool:
                return TryAdaptToBool(condition, ref adapted);
            case AnimatorControllerParameterType.Float:
                return TryAdaptToNumber(condition, isInt: false, ref adapted);
            case AnimatorControllerParameterType.Int:
                return TryAdaptToNumber(condition, isInt: true, ref adapted);
            case AnimatorControllerParameterType.Trigger:
                // A Trigger can only be tested for having fired; "has not fired" is not expressible.
                return condition.mode == AnimatorConditionMode.If;
            default:
                return false;
        }
    }

    static bool TryAdaptToBool(AnimatorCondition condition, ref AnimatorCondition adapted)
    {
        switch (condition.mode)
        {
            case AnimatorConditionMode.If:
            case AnimatorConditionMode.IfNot:
                return true;
            case AnimatorConditionMode.Greater:
                // "> t" is true only for the 1 side as long as the threshold sits below it.
                if (condition.threshold >= 1f) return false;
                adapted.mode = AnimatorConditionMode.If;
                adapted.threshold = 0f;
                return true;
            case AnimatorConditionMode.Less:
                if (condition.threshold <= 0f) return false;
                adapted.mode = AnimatorConditionMode.IfNot;
                adapted.threshold = 0f;
                return true;
            case AnimatorConditionMode.Equals:
                if (Mathf.Approximately(condition.threshold, 1f)) adapted.mode = AnimatorConditionMode.If;
                else if (Mathf.Approximately(condition.threshold, 0f)) adapted.mode = AnimatorConditionMode.IfNot;
                else return false;
                adapted.threshold = 0f;
                return true;
            case AnimatorConditionMode.NotEqual:
                if (Mathf.Approximately(condition.threshold, 1f)) adapted.mode = AnimatorConditionMode.IfNot;
                else if (Mathf.Approximately(condition.threshold, 0f)) adapted.mode = AnimatorConditionMode.If;
                else return false;
                adapted.threshold = 0f;
                return true;
            default:
                return false;
        }
    }

    static bool TryAdaptToNumber(AnimatorCondition condition, bool isInt, ref AnimatorCondition adapted)
    {
        switch (condition.mode)
        {
            case AnimatorConditionMode.Greater:
            case AnimatorConditionMode.Less:
                return true;
            case AnimatorConditionMode.Equals:
            case AnimatorConditionMode.NotEqual:
                // Only Int supports exact comparison; on a Float there is no single equivalent
                // condition (it would take two, and callers cannot add conditions here).
                return isInt;
            case AnimatorConditionMode.If:
                // "the bool is true" becomes "the number is on the 1 side".
                adapted.mode = AnimatorConditionMode.Greater;
                adapted.threshold = isInt ? 0f : 0.5f;
                return true;
            case AnimatorConditionMode.IfNot:
                adapted.mode = AnimatorConditionMode.Less;
                adapted.threshold = isInt ? 1f : 0.5f;
                return true;
            default:
                return false;
        }
    }
}
#endif

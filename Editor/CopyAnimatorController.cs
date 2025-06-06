using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;

public class CopyAnimatorController
{
    private readonly AnimatorController sourceController;
    private readonly string controllerPath;

    public CopyAnimatorController(AnimatorController sourceController)
    {
        this.sourceController = sourceController;
        this.controllerPath = AssetDatabase.GetAssetPath(sourceController);
    }

    public AnimatorController CopyController()
    {
        // Create a new AnimatorController
        var newController = new AnimatorController();
        newController.name = $"{sourceController.name}_Copy";

        CopyControllerTo(newController);

        return newController;
    }

    public void CopyControllerTo(AnimatorController targetController)
    {
        // Copy layers
        foreach (var layer in sourceController.layers)
        {
            CopyLayer(layer, targetController);
        }

        CopyParametersTo(targetController);
    }

    public void CopyLayer(AnimatorControllerLayer sourceLayer, AnimatorController targetController)
    {
        // Create a new layer
        var newLayer = new AnimatorControllerLayer
        {
            name = targetController.MakeUniqueLayerName(sourceLayer.name),
            defaultWeight = sourceLayer.defaultWeight,
            blendingMode = sourceLayer.blendingMode,
            syncedLayerIndex = sourceLayer.syncedLayerIndex,
            syncedLayerAffectsTiming = sourceLayer.syncedLayerAffectsTiming,
            iKPass = sourceLayer.iKPass,
            avatarMask = sourceLayer.avatarMask,
        };

        // Copy state machine
        if (sourceLayer.stateMachine != null)
        {
            newLayer.stateMachine = CopyStateMachine(sourceLayer.stateMachine);
        }

        // Add the layer to the target controller
        var layers = targetController.layers;
        ArrayUtility.Add(ref layers, newLayer);
        targetController.layers = layers;
    }

    public void CopyLayer(int index, AnimatorController targetController)
    {
        var sourceLayer = sourceController.layers[index];
        CopyLayer(sourceLayer, targetController);
    }

    public void CopyLayer(string name, AnimatorController targetController)
    {
        var sourceLayer = sourceController.layers.FirstOrDefault(layer => layer.name == name);
        if (sourceLayer == null)
        {
            throw new System.ArgumentException($"Layer with name {name} not found in source controller");
        }
        CopyLayer(sourceLayer, targetController);
    }

    public void CopyParametersTo(AnimatorController targetController)
    {
        foreach (var param in sourceController.parameters)
        {
            if (!targetController.parameters.Any(p => p.name == param.name))
            {
                targetController.AddParameter(new AnimatorControllerParameter
                {
                    name = param.name,
                    type = param.type,
                    defaultBool = param.defaultBool,
                    defaultInt = param.defaultInt,
                    defaultFloat = param.defaultFloat,
                });
            }
        }
    }

    private AnimatorStateMachine CopyStateMachine(AnimatorStateMachine sourceStateMachine)
    {
        var newStateMachine = new AnimatorStateMachine
        {
            name = sourceStateMachine.name,
            hideFlags = sourceStateMachine.hideFlags,
            anyStatePosition = sourceStateMachine.anyStatePosition,
            entryPosition = sourceStateMachine.entryPosition,
            exitPosition = sourceStateMachine.exitPosition,
            parentStateMachinePosition = sourceStateMachine.parentStateMachinePosition
        };

        // First pass: Create all states and sub-state machines
        var stateMapping = new Dictionary<AnimatorState, AnimatorState>();
        var subStateMachineMapping = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
        CreateStatesAndStateMachines(sourceStateMachine, newStateMachine, stateMapping, subStateMachineMapping);

        // Second pass: Copy all transitions
        CopyTransitions(sourceStateMachine, newStateMachine, stateMapping, subStateMachineMapping);

        CopyBehaviours(sourceStateMachine, newStateMachine, stateMapping, subStateMachineMapping);

        // Copy default state if it exists
        if (sourceStateMachine.defaultState != null && stateMapping.ContainsKey(sourceStateMachine.defaultState))
        {
            newStateMachine.defaultState = stateMapping[sourceStateMachine.defaultState];
        }

        return newStateMachine;
    }

    public static void CopyStateMachineBehaviourValues(StateMachineBehaviour sourceBehaviour, StateMachineBehaviour targetBehaviour)
    {
        targetBehaviour.hideFlags = sourceBehaviour.hideFlags;

        // Copy serialized fields
        var serializedObject = new SerializedObject(targetBehaviour);
        var sourceSerializedObject = new SerializedObject(sourceBehaviour);
        var property = sourceSerializedObject.GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.Generic)
            {
                serializedObject.CopyFromSerializedProperty(property);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void CreateStatesAndStateMachines(
        AnimatorStateMachine sourceStateMachine,
        AnimatorStateMachine newStateMachine,
        Dictionary<AnimatorState, AnimatorState> stateMapping,
        Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping)
    {
        // First, create all sub-state machines
        foreach (var subMachine in sourceStateMachine.stateMachines)
        {
            var newSubMachine = new AnimatorStateMachine
            {
                name = subMachine.stateMachine.name,
                hideFlags = subMachine.stateMachine.hideFlags,
                anyStatePosition = subMachine.stateMachine.anyStatePosition,
                entryPosition = subMachine.stateMachine.entryPosition,
                exitPosition = subMachine.stateMachine.exitPosition,
                parentStateMachinePosition = subMachine.stateMachine.parentStateMachinePosition
            };
            subStateMachineMapping[subMachine.stateMachine] = newSubMachine;
            var stateMachines = newStateMachine.stateMachines;
            var childStateMachine = new ChildAnimatorStateMachine
            {
                stateMachine = newSubMachine,
                position = subMachine.position
            };
            ArrayUtility.Add(ref stateMachines, childStateMachine);
            newStateMachine.stateMachines = stateMachines;
        }

        // Then create all states
        foreach (var state in sourceStateMachine.states)
        {
            var newState = CopyState(state.state);
            stateMapping[state.state] = newState;
            var states = newStateMachine.states;
            var childState = new ChildAnimatorState
            {
                state = newState,
                position = state.position
            };
            ArrayUtility.Add(ref states, childState);
            newStateMachine.states = states;
        }

        // Recursively create states and state machines for sub-state machines
        foreach (var subMachine in sourceStateMachine.stateMachines)
        {
            CreateStatesAndStateMachines(
                subMachine.stateMachine,
                subStateMachineMapping[subMachine.stateMachine],
                stateMapping,
                subStateMachineMapping
            );
        }
    }

    public static void CopyTransitions(
        AnimatorStateMachine sourceStateMachine,
        AnimatorStateMachine newStateMachine,
        Dictionary<AnimatorState, AnimatorState> stateMapping,
        Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping)
    {
        // Copy any state transitions
        foreach (var transition in sourceStateMachine.anyStateTransitions)
        {
            var newTransition = CopyTransition(transition, stateMapping, subStateMachineMapping);
            var array = newStateMachine.anyStateTransitions;
            ArrayUtility.Add(ref array, newTransition);
            newStateMachine.anyStateTransitions = array;
        }

        // Copy entry transitions
        foreach (var transition in sourceStateMachine.entryTransitions)
        {
            var newTransition = CopyTransition(transition, stateMapping, subStateMachineMapping);
            var array = newStateMachine.entryTransitions;
            ArrayUtility.Add(ref array, newTransition);
            newStateMachine.entryTransitions = array;
        }

        // Copy state transitions
        foreach (var state in sourceStateMachine.states)
        {
            var sourceState = state.state;
            var newState = stateMapping[sourceState];

            foreach (var transition in sourceState.transitions)
            {
                var newTransition = CopyTransition(transition, stateMapping, subStateMachineMapping);
                var array = newState.transitions;
                ArrayUtility.Add(ref array, newTransition);
                newState.transitions = array;
            }
        }

        // Recursively copy transitions for sub-state machines
        foreach (var subMachine in sourceStateMachine.stateMachines)
        {
            var sourceStateMachineTransitions = sourceStateMachine.GetStateMachineTransitions(subMachine.stateMachine);
            var newStateMachineTransitions = newStateMachine.GetStateMachineTransitions(subStateMachineMapping[subMachine.stateMachine]);
            foreach (var transition in sourceStateMachineTransitions)
            {
                var newTransition = CopyTransition(transition, stateMapping, subStateMachineMapping);
                ArrayUtility.Add(ref newStateMachineTransitions, newTransition);
            }
            newStateMachine.SetStateMachineTransitions(subStateMachineMapping[subMachine.stateMachine], newStateMachineTransitions);

            CopyTransitions(
                subMachine.stateMachine,
                subStateMachineMapping[subMachine.stateMachine],
                stateMapping,
                subStateMachineMapping
            );
        }
    }

    public static void CopyBehaviours(
        AnimatorStateMachine sourceStateMachine,
        AnimatorStateMachine newStateMachine,
        Dictionary<AnimatorState, AnimatorState> stateMapping,
        Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping)
    {
        foreach (var behaviour in sourceStateMachine.behaviours)
        {
            var newBehaviour = newStateMachine.AddStateMachineBehaviour(behaviour.GetType());
            CopyStateMachineBehaviourValues(behaviour, newBehaviour);
        }
        foreach (var state in sourceStateMachine.states)
        {
            foreach (var behaviour in state.state.behaviours)
            {
                var newBehaviour = stateMapping[state.state].AddStateMachineBehaviour(behaviour.GetType());
                CopyStateMachineBehaviourValues(behaviour, newBehaviour);
            }
        }
        foreach (var subMachine in sourceStateMachine.stateMachines)
        {
            CopyBehaviours(
                subMachine.stateMachine,
                subStateMachineMapping[subMachine.stateMachine],
                stateMapping,
                subStateMachineMapping
            );
        }
    }

    private AnimatorState CopyState(AnimatorState sourceState)
    {
        var newState = new AnimatorState
        {
            name = sourceState.name,
            speed = sourceState.speed,
            cycleOffset = sourceState.cycleOffset,
            iKOnFeet = sourceState.iKOnFeet,
            mirror = sourceState.mirror,
            mirrorParameterActive = sourceState.mirrorParameterActive,
            cycleOffsetParameterActive = sourceState.cycleOffsetParameterActive,
            timeParameterActive = sourceState.timeParameterActive,
            tag = sourceState.tag,
            speedParameterActive = sourceState.speedParameterActive,
            cycleOffsetParameter = sourceState.cycleOffsetParameter,
            timeParameter = sourceState.timeParameter,
            speedParameter = sourceState.speedParameter,
            hideFlags = sourceState.hideFlags,
            mirrorParameter = sourceState.mirrorParameter,
            writeDefaultValues = sourceState.writeDefaultValues,
        };

        if (sourceState.motion is BlendTree sourceBlendTree && !IsBlendTreePersisted(sourceBlendTree))
        {
            newState.motion = CopyBlendTree(sourceBlendTree);
        }
        else if (sourceState.motion is AnimationClip sourceClip && !IsAnimationClipPersisted(sourceClip))
        {
            newState.motion = CopyAnimationClip(sourceClip);
        }
        else
        {
            newState.motion = sourceState.motion;
        }

        return newState;
    }

    private bool IsBlendTreePersisted(BlendTree blendTree) => IsBlendTreePersisted(controllerPath, blendTree);

    public static bool IsBlendTreePersisted(string controllerPath, BlendTree blendTree)
    {
        var blendTreePath = AssetDatabase.GetAssetPath(blendTree);
        if (string.IsNullOrEmpty(blendTreePath)) return false;

        // Not persisted in the same file
        return blendTreePath != controllerPath;
    }

    private BlendTree CopyBlendTree(BlendTree sourceBlendTree)
    {
        return CopyBlendTree(controllerPath, sourceBlendTree, true);
    }

    public static BlendTree CopyBlendTree(string controllerPath, BlendTree sourceBlendTree, bool copyDeep)
    {
        var newBlendTree = new BlendTree
        {
            name = sourceBlendTree.name,
            hideFlags = sourceBlendTree.hideFlags,
            blendType = sourceBlendTree.blendType,
            blendParameter = sourceBlendTree.blendParameter,
            blendParameterY = sourceBlendTree.blendParameterY,
            minThreshold = sourceBlendTree.minThreshold,
            maxThreshold = sourceBlendTree.maxThreshold,
            useAutomaticThresholds = sourceBlendTree.useAutomaticThresholds,
        };

        foreach (var childMotion in sourceBlendTree.children)
        {
            var newChildMotion = new ChildMotion
            {
                timeScale = childMotion.timeScale,
                threshold = childMotion.threshold,
                directBlendParameter = childMotion.directBlendParameter,
                mirror = childMotion.mirror,
                cycleOffset = childMotion.cycleOffset,
                position = childMotion.position,
            };

            if (copyDeep && childMotion.motion is BlendTree childBlendTree && !IsBlendTreePersisted(controllerPath, childBlendTree))
            {
                newChildMotion.motion = CopyBlendTree(controllerPath, childBlendTree, copyDeep);
            }
            else if (copyDeep && childMotion.motion is AnimationClip childClip && !IsAnimationClipPersisted(controllerPath, childClip))
            {
                newChildMotion.motion = CopyAnimationClip(childClip);
            }
            else
            {
                newChildMotion.motion = childMotion.motion;
            }

            var children = newBlendTree.children;
            ArrayUtility.Add(ref children, newChildMotion);
            newBlendTree.children = children;
        }

        return newBlendTree;
    }

    private bool IsAnimationClipPersisted(AnimationClip clip) => IsAnimationClipPersisted(controllerPath, clip);

    public static bool IsAnimationClipPersisted(string controllerPath, AnimationClip clip)
    {
        var clipPath = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(clipPath)) return false;
        // Not persisted in the same file
        return clipPath != controllerPath;
    }

    public static AnimationClip CopyAnimationClip(AnimationClip sourceClip)
    {
        var newClip = new AnimationClip
        {
            name = sourceClip.name,
            hideFlags = sourceClip.hideFlags,
            frameRate = sourceClip.frameRate,
            legacy = sourceClip.legacy,
            wrapMode = sourceClip.wrapMode,
            localBounds = sourceClip.localBounds,
        };
        AnimationUtility.SetAnimationClipSettings(newClip, AnimationUtility.GetAnimationClipSettings(sourceClip));
        foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
        {
            var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            newClip.SetCurve(binding.path, binding.type, binding.propertyName, curve);
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(sourceClip))
        {
            var objectReferenceCurve = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
            AnimationUtility.SetObjectReferenceCurve(newClip, binding, objectReferenceCurve);
        }
        AnimationUtility.SetAnimationEvents(newClip, AnimationUtility.GetAnimationEvents(sourceClip).Select(ev =>
        new AnimationEvent
        {
            time = ev.time,
            functionName = ev.functionName,
            stringParameter = ev.stringParameter,
            objectReferenceParameter = ev.objectReferenceParameter,
            floatParameter = ev.floatParameter,
            intParameter = ev.intParameter,
            messageOptions = ev.messageOptions,
        }).ToArray());

        return newClip;
    }

    public static AnimatorStateTransition CopyTransition(
        AnimatorStateTransition sourceTransition,
        Dictionary<AnimatorState, AnimatorState> stateMapping,
        Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping)
    {
        var newTransition = new AnimatorStateTransition
        {
            hasExitTime = sourceTransition.hasExitTime,
            exitTime = sourceTransition.exitTime,
            hasFixedDuration = sourceTransition.hasFixedDuration,
            duration = sourceTransition.duration,
            offset = sourceTransition.offset,
            interruptionSource = sourceTransition.interruptionSource,
            orderedInterruption = sourceTransition.orderedInterruption,
            canTransitionToSelf = sourceTransition.canTransitionToSelf,
            solo = sourceTransition.solo,
            mute = sourceTransition.mute,
            name = sourceTransition.name,
            isExit = sourceTransition.isExit,
            hideFlags = sourceTransition.hideFlags,
        };

        // Set destination state or state machine
        if (sourceTransition.destinationState != null)
        {
            newTransition.destinationState = stateMapping[sourceTransition.destinationState];
        }
        else if (sourceTransition.destinationStateMachine != null)
        {
            newTransition.destinationStateMachine = subStateMachineMapping[sourceTransition.destinationStateMachine];
        }

        // Copy conditions
        foreach (var condition in sourceTransition.conditions)
        {
            var newCondition = new AnimatorCondition
            {
                mode = condition.mode,
                threshold = condition.threshold,
                parameter = condition.parameter,
            };
            var conditions = sourceTransition.conditions;
            ArrayUtility.Add(ref conditions, newCondition);
            newTransition.conditions = conditions;
        }

        return newTransition;
    }

    public static AnimatorTransition CopyTransition(
        AnimatorTransition sourceTransition,
        Dictionary<AnimatorState, AnimatorState> stateMapping,
        Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping)
    {
        var newTransition = new AnimatorTransition
        {
            hideFlags = sourceTransition.hideFlags,
            isExit = sourceTransition.isExit,
            solo = sourceTransition.solo,
            mute = sourceTransition.mute,
            name = sourceTransition.name,
        };

        // Set destination state or state machine
        if (sourceTransition.destinationState != null)
        {
            newTransition.destinationState = stateMapping[sourceTransition.destinationState];
        }
        else if (sourceTransition.destinationStateMachine != null)
        {
            newTransition.destinationStateMachine = subStateMachineMapping[sourceTransition.destinationStateMachine];
        }

        // Copy conditions
        foreach (var condition in sourceTransition.conditions)
        {
            var newCondition = new AnimatorCondition
            {
                mode = condition.mode,
                threshold = condition.threshold,
                parameter = condition.parameter,
            };
            var conditions = sourceTransition.conditions;
            ArrayUtility.Add(ref conditions, newCondition);
            newTransition.conditions = conditions;
        }

        return newTransition;
    }
}

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
            name = sourceLayer.name,
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
                targetController.AddParameter(param.name, param.type);
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

        // Copy StateMachineBehaviours
        foreach (var behaviour in sourceStateMachine.behaviours)
        {
            var newBehaviour = newStateMachine.AddStateMachineBehaviour(behaviour.GetType());
            CopyStateMachineBehaviourValues(behaviour, newBehaviour);
        }

        // First pass: Create all states and sub-state machines
        var stateMapping = new Dictionary<AnimatorState, AnimatorState>();
        var subStateMachineMapping = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
        CreateStatesAndStateMachines(sourceStateMachine, newStateMachine, stateMapping, subStateMachineMapping);

        // Second pass: Copy all transitions
        CopyTransitions(sourceStateMachine, newStateMachine, stateMapping, subStateMachineMapping);

        // Copy default state if it exists
        if (sourceStateMachine.defaultState != null && stateMapping.ContainsKey(sourceStateMachine.defaultState))
        {
            newStateMachine.defaultState = stateMapping[sourceStateMachine.defaultState];
        }

        return newStateMachine;
    }

    private void CopyStateMachineBehaviourValues(StateMachineBehaviour sourceBehaviour, StateMachineBehaviour targetBehaviour)
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

    private void CopyTransitions(
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
            CopyTransitions(
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

        // Copy motion if it's a BlendTree and it's persisted in the controller
        if (sourceState.motion is BlendTree sourceBlendTree && IsBlendTreePersisted(sourceBlendTree))
        {
            newState.motion = CopyBlendTree(sourceBlendTree);
        }
        else
        {
            newState.motion = sourceState.motion;
        }

        return newState;
    }

    private bool IsBlendTreePersisted(BlendTree blendTree)
    {
        if (string.IsNullOrEmpty(controllerPath))
            return false;

        // BlendTreeのパスを取得
        string blendTreePath = AssetDatabase.GetAssetPath(blendTree);
        if (string.IsNullOrEmpty(blendTreePath))
            return false;

        // 同じファイル内に永続化されているかどうかを判定
        return blendTreePath == controllerPath;
    }

    private BlendTree CopyBlendTree(BlendTree sourceBlendTree)
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

        // Copy all child motions recursively
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

            // Recursively copy child BlendTree if it exists and is persisted
            if (childMotion.motion is BlendTree childBlendTree && IsBlendTreePersisted(childBlendTree))
            {
                newChildMotion.motion = CopyBlendTree(childBlendTree);
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

    private AnimatorStateTransition CopyTransition(
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

    private AnimatorTransition CopyTransition(
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

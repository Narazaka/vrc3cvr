using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;

public class CopyAnimatorController
{
    private readonly AnimatorController sourceController;

    public CopyAnimatorController(AnimatorController sourceController)
    {
        this.sourceController = sourceController;
    }

    public AnimatorController CopyController()
    {
        // Create a new AnimatorController
        var newController = new AnimatorController();
        newController.name = $"{sourceController.name}_Copy";

        // Copy layers
        foreach (var layer in sourceController.layers)
        {
            CopyLayer(layer, newController);
        }

        // Copy parameters
        foreach (var param in sourceController.parameters)
        {
            newController.AddParameter(param.name, param.type);
        }

        return newController;
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
            avatarMask = sourceLayer.avatarMask
        };

        // Copy state machine
        if (sourceLayer.stateMachine != null)
        {
            newLayer.stateMachine = CopyStateMachine(sourceLayer.stateMachine);
        }

        // Add the layer to the target controller
        targetController.AddLayer(newLayer);
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

        return newStateMachine;
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
            newStateMachine.AddStateMachine(newSubMachine, subMachine.position);
        }

        // Then create all states
        foreach (var state in sourceStateMachine.states)
        {
            var newState = CopyState(state.state);
            stateMapping[state.state] = newState;
            newStateMachine.AddState(newState, state.position);
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
            newStateMachine.AddAnyStateTransition(newTransition);
        }

        // Copy entry transitions
        foreach (var transition in sourceStateMachine.entryTransitions)
        {
            var newTransition = CopyTransition(transition, stateMapping, subStateMachineMapping);
            newStateMachine.AddEntryTransition(newTransition);
        }

        // Copy state transitions
        foreach (var state in sourceStateMachine.states)
        {
            var sourceState = state.state;
            var newState = stateMapping[sourceState];

            foreach (var transition in sourceState.transitions)
            {
                var newTransition = CopyTransition(transition, stateMapping, subStateMachineMapping);
                newState.AddTransition(newTransition);
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
            motion = sourceState.motion,
            tag = sourceState.tag,
            speedParameterActive = sourceState.speedParameterActive,
            cycleOffsetParameter = sourceState.cycleOffsetParameter,
            timeParameter = sourceState.timeParameter,
            speedParameter = sourceState.speedParameter
        };

        return newState;
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
            newTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
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
            newTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
        }

        return newTransition;
    }
}

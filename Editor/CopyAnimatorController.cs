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

        // Copy states and sub-state machines
        var stateMapping = new Dictionary<AnimatorState, AnimatorState>();
        var subStateMachineMapping = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
        
        // First pass: Create all states and sub-state machines
        foreach (var state in sourceStateMachine.states)
        {
            if (state.state.motion is AnimatorStateMachine subMachine)
            {
                // Create a new sub-state machine
                var newSubMachine = CopyStateMachine(subMachine);
                subStateMachineMapping[subMachine] = newSubMachine;
                
                // Create a state that references the new sub-state machine
                var newState = new AnimatorState
                {
                    name = state.state.name,
                    motion = newSubMachine
                };
                stateMapping[state.state] = newState;
            }
            else
            {
                var newState = CopyState(state.state);
                stateMapping[state.state] = newState;
            }
            newStateMachine.AddState(stateMapping[state.state], state.position);
        }

        // Copy transitions
        foreach (var transition in sourceStateMachine.anyStateTransitions)
        {
            var newTransition = CopyTransition(transition, stateMapping);
            newStateMachine.AddAnyStateTransition(newTransition);
        }

        foreach (var transition in sourceStateMachine.entryTransitions)
        {
            var newTransition = CopyTransition(transition, stateMapping);
            newStateMachine.AddEntryTransition(newTransition);
        }

        // Copy state transitions
        foreach (var state in sourceStateMachine.states)
        {
            var sourceState = state.state;
            var newState = stateMapping[sourceState];

            foreach (var transition in sourceState.transitions)
            {
                var newTransition = CopyTransition(transition, stateMapping);
                newState.AddTransition(newTransition);
            }
        }

        return newStateMachine;
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

    private AnimatorStateTransition CopyTransition(AnimatorStateTransition sourceTransition, Dictionary<AnimatorState, AnimatorState> stateMapping)
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
            exitTimeParameter = sourceTransition.exitTimeParameter,
            exitTimeParameterActive = sourceTransition.exitTimeParameterActive,
            destinationState = stateMapping[sourceTransition.destinationState]
        };

        // Copy conditions
        foreach (var condition in sourceTransition.conditions)
        {
            newTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
        }

        return newTransition;
    }

    private AnimatorStateTransition CopyTransition(AnimatorTransition sourceTransition, Dictionary<AnimatorState, AnimatorState> stateMapping)
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
            exitTimeParameter = sourceTransition.exitTimeParameter,
            exitTimeParameterActive = sourceTransition.exitTimeParameterActive
        };

        // Copy conditions
        foreach (var condition in sourceTransition.conditions)
        {
            newTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
        }

        return newTransition;
    }
}

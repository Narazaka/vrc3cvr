using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class SaveAnimatorController
{
    private readonly AnimatorController controller;

    public SaveAnimatorController(AnimatorController controller)
    {
        this.controller = controller;
    }

    public void Save(string path = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(path))
            {
                throw new System.ArgumentException("Save path must be specified for newly created AnimatorController.");
            }
        }

        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(controller)))
        {
            AssetDatabase.CreateAsset(controller, path);
        }
        EditorUtility.SetDirty(controller);

        foreach (var layer in controller.layers)
        {
            SaveStateMachine(layer.stateMachine);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void SaveStateMachine(AnimatorStateMachine stateMachine)
    {
        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(stateMachine)))
        {
            AssetDatabase.AddObjectToAsset(stateMachine, controller);
        }
        foreach (var behaviour in stateMachine.behaviours)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(behaviour)))
            {
                AssetDatabase.AddObjectToAsset(behaviour, controller);
            }
        }

        foreach (var transition in stateMachine.anyStateTransitions)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(transition)))
            {
                AssetDatabase.AddObjectToAsset(transition, controller);
            }
        }

        foreach (var transition in stateMachine.entryTransitions)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(transition)))
            {
                AssetDatabase.AddObjectToAsset(transition, controller);
            }
        }

        foreach (var state in stateMachine.states)
        {
            SaveState(state.state);
        }

        foreach (var subMachine in stateMachine.stateMachines)
        {
            SaveStateMachine(subMachine.stateMachine);
        }
    }

    private void SaveState(AnimatorState state)
    {
        foreach (var behaviour in state.behaviours)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(behaviour)))
            {
                AssetDatabase.AddObjectToAsset(behaviour, controller);
            }
        }

        foreach (var transition in state.transitions)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(transition)))
            {
                AssetDatabase.AddObjectToAsset(transition, controller);
            }
        }

        if (state.motion is BlendTree blendTree)
        {
            SaveBlendTree(blendTree);
        }

        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(state)))
        {
            AssetDatabase.AddObjectToAsset(state, controller);
        }
    }

    private void SaveBlendTree(BlendTree blendTree)
    {
        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(blendTree)))
        {
            AssetDatabase.AddObjectToAsset(blendTree, controller);
        }

        foreach (var childMotion in blendTree.children)
        {
            if (childMotion.motion is BlendTree childBlendTree)
            {
                SaveBlendTree(childBlendTree);
            }
        }
    }
}

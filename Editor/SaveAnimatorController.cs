using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;

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
        }

        // 各レイヤーのステートマシンとその中のステートを永続化
        foreach (var layer in controller.layers)
        {
            if (layer.stateMachine != null)
            {
                SaveStateMachine(layer.stateMachine);
            }
        }

        // コントローラー自体を永続化
        AssetDatabase.SaveAssets();
    }

    private void SaveStateMachine(AnimatorStateMachine stateMachine)
    {
        // ステートマシンのビヘイビアを永続化
        foreach (var behaviour in stateMachine.behaviours)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(behaviour)))
            {
                AssetDatabase.AddObjectToAsset(behaviour, controller);
            }
        }

        // AnyState transitionsを永続化
        foreach (var transition in stateMachine.anyStateTransitions)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(transition)))
            {
                AssetDatabase.AddObjectToAsset(transition, controller);
            }
        }

        // Entry transitionsを永続化
        foreach (var transition in stateMachine.entryTransitions)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(transition)))
            {
                AssetDatabase.AddObjectToAsset(transition, controller);
            }
        }

        // 各ステートを永続化
        foreach (var state in stateMachine.states)
        {
            SaveState(state.state);
        }

        // サブステートマシンを永続化
        foreach (var subMachine in stateMachine.stateMachines)
        {
            SaveStateMachine(subMachine.stateMachine);
        }
    }

    private void SaveState(AnimatorState state)
    {
        // ステートのビヘイビアを永続化
        foreach (var behaviour in state.behaviours)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(behaviour)))
            {
                AssetDatabase.AddObjectToAsset(behaviour, controller);
            }
        }

        // ステートのtransitionsを永続化
        foreach (var transition in state.transitions)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(transition)))
            {
                AssetDatabase.AddObjectToAsset(transition, controller);
            }
        }

        // BlendTreeの永続化
        if (state.motion is BlendTree blendTree)
        {
            SaveBlendTree(blendTree);
        }

        // ステート自体を永続化
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

        // 子のBlendTreeを再帰的に永続化
        foreach (var childMotion in blendTree.children)
        {
            if (childMotion.motion is BlendTree childBlendTree)
            {
                SaveBlendTree(childBlendTree);
            }
        }
    }
}

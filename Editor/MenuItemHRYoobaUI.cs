﻿using UnityEngine;
using UnityEditor;

namespace HRYooba.Library.Editor
{
    public class MenuItemHRYoobaUI
    {
        private static void InstantiateGameObject(MenuCommand menuCommand, string path)
        {
            // Create a custom game object
            GameObject obj = Resources.Load<GameObject>(path);
            GameObject go = UnityEngine.GameObject.Instantiate(obj) as GameObject;
            go.name = obj.name;
            // Ensure it gets reparented if this was a context click (otherwise does nothing)
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            // Register the creation in the undo system
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/UI/HRYooba/OperationCanvas", false, 21)]
        private static void CreateOperationCanvas(MenuCommand menuCommand)
        {
            InstantiateGameObject(menuCommand, "OperationCanvas");
        }

        [MenuItem("GameObject/UI/HRYooba/IntController", false, 21)]
        private static void CreateIntController(MenuCommand menuCommand)
        {
            InstantiateGameObject(menuCommand, "IntController");
        }

        [MenuItem("GameObject/UI/HRYooba/FloatController", false, 21)]
        private static void CreateFloatController(MenuCommand menuCommand)
        {
            InstantiateGameObject(menuCommand, "FloatController");
        }

        [MenuItem("GameObject/UI/HRYooba/Vector2Controller", false, 21)]
        private static void CreateVector2Controller(MenuCommand menuCommand)
        {
            InstantiateGameObject(menuCommand, "Vector2Controller");
        }

        [MenuItem("GameObject/UI/HRYooba/Vector3Controller", false, 21)]
        private static void CreateVector3Controller(MenuCommand menuCommand)
        {
            InstantiateGameObject(menuCommand, "Vector3Controller");
        }

        [MenuItem("GameObject/UI/HRYooba/Vector4Controller", false, 21)]
        private static void CreateVector4Controller(MenuCommand menuCommand)
        {
            InstantiateGameObject(menuCommand, "Vector4Controller");
        }

        [MenuItem("GameObject/UI/HRYooba/Button", false, 21)]
        private static void CreateButton(MenuCommand menuCommand)
        {
            InstantiateGameObject(menuCommand, "Button");
        }
    }
}
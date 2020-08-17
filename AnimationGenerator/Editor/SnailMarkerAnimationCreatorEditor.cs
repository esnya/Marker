using System.Collections;
using System.Collections.Generic;
using System.Linq;


using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

[CustomEditor(typeof(SnailMarkerAnimationCreator))]
public class SnailMarkerAnimationCreatorEditor : Editor
{
    public static readonly string TEMPLATE_PATH = "Assets/VRCSDK/Examples/Sample Assets/Animation/AvatarControllerTemplate.controller";
    SnailMarkerAnimationCreator obj;
    public void OnEnable()
    {
        obj = (SnailMarkerAnimationCreator)target;
    }
    public override void OnInspectorGUI()
    {
        // if (GUILayout.Button("Debug") && findAvatarAndAnimationPath(obj.transform)) {
        //     var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
        //     Debug.Log(descriptor.expressionParameters.parameters.Count(p => p.name == "MarkerState"));
        // }

        if (!GUILayout.Button("Do everything"))
            return;
        DoEverything();
    }


    Transform avatar = null;
    string animationPath;
    string exportPath;

    private bool findAvatarAndAnimationPath(Transform cur)
    {
        // Find the avatar root and record the animation path along the way.
        string path = "";
        do
        {
            if (cur.GetComponent<VRCAvatarDescriptor>() != null)
            {
                avatar = cur;
                break;
            }
            if(path.Length > 0 )
                path = cur.name + "/" + path;
            else
                path = cur.name;
            cur = cur.parent;
        } while (cur != null);

        if (avatar != null)
        {
            animationPath = path;
            Debug.Log("Animation path:" + animationPath);
            return true;
        }
        return false;
    }

    private bool getExportPath()
    {
        exportPath = EditorUtility.OpenFolderPanel("Save Generated Animations", "", "");
        // "c:/Users/snail/Downloads/VR Chat/Assets/Snail/Marker";

        if (exportPath.Length == 0)
            return false;
        int pathSplitPos = exportPath.IndexOf("/Assets");
        if (pathSplitPos == -1)
        {
            Debug.LogError("'/Assets' not found in path. Export path needs to be inside your project.");
            return false;
        }
        // Make exportPath have the form "Assets/..../"
        exportPath = exportPath.Substring(pathSplitPos + 1) + "/";
        return true;
    }


    public void DoEverything()
    {
        if (!findAvatarAndAnimationPath(obj.transform))
        {
            // We need the avatar descriptor for overrides.
            // Animations are also relative paths from the avatar descriptor.
            Debug.LogError("Could not find Avatar Descriptor component.");
        }

        if (!getExportPath())
        {
            // We have to write the animation files and overrides somewhere.
            Debug.LogError("Could not get a valid export path.");
            return;
        }

        DuplicateMaterial();
        WriteAnimations();
        SetupLayer();
        SetupExpressionParameters();
        SetupExpressionMenu();
        Cleanup();
    }

    private void DuplicateMaterial()
    {
        // Duplicating the material so that the user can have different colors
        // across different instances of the marker.
        string materialPath = exportPath + "Ink.mat";
        TrailRenderer r = obj.gameObject.GetComponent<TrailRenderer>();
        WriteAsset(r.material, materialPath);
        r.material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    }

    private void WriteAnimations()
    {
        float keyframe = 1F / 60;
        // Curve that sets a property to 0 over the course of 1 frame.
        AnimationCurve zeroCurve = AnimationCurve.Linear(0, 0, keyframe, 0);
        zeroCurve.AddKey(new Keyframe(keyframe, 0));
        AnimationClip erase = new AnimationClip();
        erase.SetCurve(animationPath, typeof(TrailRenderer), "m_Time", zeroCurve);
        WriteAsset(erase, exportPath + "EraseAll.anim");

        AnimationClip draw = new AnimationClip();
        draw.SetCurve(animationPath, typeof(Transform), "m_LocalPosition.x", zeroCurve);
        draw.SetCurve(animationPath, typeof(Transform), "m_LocalPosition.y", zeroCurve);
        draw.SetCurve(animationPath, typeof(Transform), "m_LocalPosition.z", zeroCurve);
        WriteAsset(draw, exportPath + "Drawing.anim");
    }

    private AnimatorState AddState(AnimatorStateMachine stateMachine, string name, AnimationClip clip, int threshold) {
        var state = new AnimatorState(){
            name = name,
            motion = clip,
        };
        stateMachine.AddState(state, Vector3.zero);

        var transition = stateMachine.AddAnyStateTransition(state);
        transition.conditions = new [] {
            new AnimatorCondition() {
                mode = AnimatorConditionMode.Equals,
                parameter = "MarkerState",
                threshold = threshold,
            },
        };
        transition.canTransitionToSelf = false;
        transition.duration = 0;
        transition.hasExitTime = false;

        return state;
    }
    private void SetupLayer()
    {
        VRCAvatarDescriptor descriptor = avatar.gameObject.GetComponent<VRCAvatarDescriptor>();
        descriptor.customizeAnimationLayers = true;

        var found = descriptor.baseAnimationLayers.FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
        var controller = found;

        if (controller == null) {
            controller = new AnimatorController();
            WriteAsset(controller, exportPath + "FXLayer.controller");
        } else if (AssetDatabase.GetAssetPath(controller).StartsWith("/Assets/VRCSDK/Examples3/BlendTrees/Controllers")) {
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(controller), exportPath + "FXLayer.controller");
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(exportPath + "FXLayer.controller");
        }
        if (found != controller) {
            descriptor.baseAnimationLayers = descriptor.baseAnimationLayers
                .Select(layer => {
                    if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX) return layer;

                    return new VRCAvatarDescriptor.CustomAnimLayer() {
                        type = VRCAvatarDescriptor.AnimLayerType.FX,
                        isDefault = false,
                        isEnabled = true,
                        mask = null,
                        animatorController = controller,
                    };
                })
                .ToArray();
        }

        if (controller.parameters.Count(p => p.name == "MarkerState") > 0 || controller.layers.Count(l => l.name == "Marker") > 0) {
            Debug.Log("Skipping to setup FX layer controller. Please setup manually.");
            return;
        }

        controller.AddParameter("MarkerState", AnimatorControllerParameterType.Int);

        controller.AddLayer("Marker");
        var markerLayer = controller.layers.FirstOrDefault(l => l.name == "Marker");
        markerLayer.defaultWeight = 1.0f;

        markerLayer.stateMachine.defaultState = AddState(markerLayer.stateMachine, "Wait", null, 0);
        AddState(markerLayer.stateMachine, "Drawing", AssetDatabase.LoadAssetAtPath<AnimationClip>(exportPath + "Drawing.anim"), 1);
        AddState(markerLayer.stateMachine, "Erase All", AssetDatabase.LoadAssetAtPath<AnimationClip>(exportPath + "EraseAll.anim"), 2);

        AssetDatabase.SaveAssets();
    }

    private void SetupExpressionParameters() {
        VRCAvatarDescriptor descriptor = avatar.gameObject.GetComponent<VRCAvatarDescriptor>();
        descriptor.customExpressions = true;

        var parameters = descriptor.expressionParameters;
        if (parameters == null) {
            AssetDatabase.CopyAsset(AssetDatabase.FindAssets("MarkerParametersTemplate t:VRCExpressionParameters").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(), exportPath + "Parameters.asset");
            parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(exportPath + "Parameters.asset");
            descriptor.expressionParameters = parameters;
        } else if (parameters.parameters.Count(p => p.name == "MarkerState") == 0) {
            try {
                var emptyIndex = parameters.parameters.Select((p, i) => new { i, p.name }).First(a => a.name == null || a.name == "").i;
                parameters.parameters = parameters.parameters.Select((p, i) => {
                    if (i != emptyIndex) return p;
                    return new VRCExpressionParameters.Parameter() {
                        name = "MarkerState",
                        valueType = VRCExpressionParameters.ValueType.Int,
                    };
                }).ToArray();
            } catch {
                Debug.Log("Skipping to add parameter `MarkerState`. Please add manually.");
            }
        }
    }
    private void SetupExpressionMenu() {
        VRCAvatarDescriptor descriptor = avatar.gameObject.GetComponent<VRCAvatarDescriptor>();
        descriptor.customExpressions = true;

        AssetDatabase.CopyAsset(AssetDatabase.FindAssets("MarkerMenuTemplate t:VRCExpressionsMenu").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(), exportPath + "MarkerMenu.asset");
        var markerMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(exportPath + "MarkerMenu.asset");
        AssetDatabase.SaveAssets();

        var menu = descriptor.expressionsMenu;
        if (menu == null) {
            descriptor.expressionsMenu = markerMenu;
        } else {
            menu.controls = menu.controls.Append(new VRCExpressionsMenu.Control() {
                name = "Marker",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = markerMenu
            }).ToList();
        }
    }

    private void Cleanup()
    {
        // Remove this script from the avatar so that VRC is happy.
        DestroyImmediate(obj.gameObject.GetComponent<SnailMarkerAnimationCreator>());
    }

    private void WriteAsset(Object asset, string path)
    {
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
    }
}

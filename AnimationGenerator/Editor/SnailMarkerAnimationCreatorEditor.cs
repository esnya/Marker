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
        var parameterName = SetupLayer();
        SetupExpressionParameters(parameterName);
        SetupExpressionMenu(parameterName);
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

    private AnimatorState AddState(string assetPath, AnimatorStateMachine stateMachine, string name, AnimationClip clip, int threshold, string parameterName) {
        var state = new AnimatorState(){
            name = name,
            motion = clip,
        };
        stateMachine.AddState(state, Vector3.zero);
        AssetDatabase.AddObjectToAsset(state, assetPath);

        var transition = stateMachine.AddAnyStateTransition(state);
        transition.conditions = new [] {
            new AnimatorCondition() {
                mode = AnimatorConditionMode.Equals,
                parameter = parameterName,
                threshold = threshold,
            },
        };
        transition.canTransitionToSelf = false;
        transition.duration = 0;
        transition.hasExitTime = false;
        AssetDatabase.AddObjectToAsset(transition, assetPath);

        return state;
    }
    private string SetupLayer()
    {
        VRCAvatarDescriptor descriptor = avatar.gameObject.GetComponent<VRCAvatarDescriptor>();
        descriptor.customizeAnimationLayers = true;

        var found = descriptor.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
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
                .Select(l => {
                    if (l.type != VRCAvatarDescriptor.AnimLayerType.FX) return l;

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

        var assetPath = AssetDatabase.GetAssetPath(controller);

        var parameterName = controller.MakeUniqueParameterName("MarkerState");
        controller.AddParameter(parameterName, AnimatorControllerParameterType.Int);

        var markerLayer = new AnimatorControllerLayer();
        markerLayer.name = controller.MakeUniqueLayerName("Marker");
        markerLayer.stateMachine = new AnimatorStateMachine();
        markerLayer.stateMachine.name = markerLayer.name;
        markerLayer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
        markerLayer.defaultWeight = 1.0f;

        markerLayer.stateMachine.defaultState = AddState(assetPath, markerLayer.stateMachine, "Wait", null, 0, parameterName);
        AddState(assetPath, markerLayer.stateMachine, "Drawing", AssetDatabase.LoadAssetAtPath<AnimationClip>(exportPath + "Drawing.anim"), 1, parameterName);
        AddState(assetPath, markerLayer.stateMachine, "Erase All", AssetDatabase.LoadAssetAtPath<AnimationClip>(exportPath + "EraseAll.anim"), 2, parameterName);

        AssetDatabase.AddObjectToAsset(markerLayer.stateMachine, assetPath);
        controller.AddLayer(markerLayer);

        return parameterName;
    }

    private void SetupExpressionParameters(string parameterName) {
        VRCAvatarDescriptor descriptor = avatar.gameObject.GetComponent<VRCAvatarDescriptor>();
        descriptor.customExpressions = true;

        var parameters = descriptor.expressionParameters;
        if (parameters == null) {
            AssetDatabase.CopyAsset(AssetDatabase.FindAssets("MarkerParametersTemplate t:VRCExpressionParameters").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(), exportPath + "Parameters.asset");
            parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(exportPath + "Parameters.asset");
            descriptor.expressionParameters = parameters;
        }

        var emptySlot = ArrayUtility.FindIndex(parameters.parameters, p => string.IsNullOrEmpty(p.name));
        if (emptySlot < 0) {
            Debug.LogError("Parameter slots are full.");
            return;
        }

        parameters.parameters = parameters.parameters.Select((p, i) => i == emptySlot ? new VRCExpressionParameters.Parameter() {
            name = parameterName,
            valueType = VRCExpressionParameters.ValueType.Int,
        } : p).ToArray();
        EditorUtility.SetDirty(parameters);
    }

    private void SetupExpressionMenu(string parameterName) {
        VRCAvatarDescriptor descriptor = avatar.gameObject.GetComponent<VRCAvatarDescriptor>();
        descriptor.customExpressions = true;

        var markerMenu = new VRCExpressionsMenu() {
            name = "Marker",
            controls = new List<VRCExpressionsMenu.Control>(),
        };
        markerMenu.controls.Add(new VRCExpressionsMenu.Control() {
            name = "Draw",
            type = VRCExpressionsMenu.Control.ControlType.Button,
            parameter = new VRCExpressionsMenu.Control.Parameter() {
                name = parameterName,
            },
            value = 1,
        });
        markerMenu.controls.Add(new VRCExpressionsMenu.Control() {
            name = "Erase All",
            type = VRCExpressionsMenu.Control.ControlType.Button,
            parameter = new VRCExpressionsMenu.Control.Parameter() {
                name = parameterName,
            },
            value = 2,
        });

        WriteAsset(markerMenu, exportPath + "MarkerMenu.asset");

        var menu = descriptor.expressionsMenu;
        if (menu == null) {
            descriptor.expressionsMenu = markerMenu;
        } else {
            menu.controls.Add(new VRCExpressionsMenu.Control() {
                name = "Marker",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = markerMenu
            });
            EditorUtility.SetDirty(menu);
        }
    }

    private void Cleanup()
    {
        // Remove this script from the avatar so that VRC is happy.
        DestroyImmediate(obj.gameObject.GetComponent<SnailMarkerAnimationCreator>());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void WriteAsset(Object asset, string path)
    {
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
    }
}

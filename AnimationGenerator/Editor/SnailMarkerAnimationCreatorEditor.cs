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
    private static Texture2D FindTexture(string name) {
        return AssetDatabase.FindAssets($"t:Texture2D {name}").Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Contains("/Marker/")).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();
    }

    SnailMarkerAnimationCreator obj;
    public void OnEnable()
    {
        obj = (SnailMarkerAnimationCreator)target;
    }
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

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

    private AnimatorState AddState(AnimatorStateMachine stateMachine, string name, AnimationClip clip = null, IEnumerable<AnimatorCondition> enterConditions = null, IEnumerable<AnimatorCondition> exitConditions = null) {
        var state = stateMachine.AddState(name, new Vector3(200, 100 * stateMachine.states.Length, 0));
        state.motion = clip;

        if (enterConditions != null) {
            var enter = stateMachine.AddAnyStateTransition(state);
            enter.canTransitionToSelf = false;
            enter.conditions = enterConditions.ToArray();
            enter.destinationState = state;
            enter.duration = 0.0f;
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
        }

        if (exitConditions != null) {
            exitConditions.ToList().ForEach(c => {
                var exit = state.AddExitTransition();
                exit.conditions = new [] { c };
                exit.duration = 0.0f;
                exit.hasExitTime = false;
                exit.hasFixedDuration = true;
            });
        }

        return state;
    }

    private AnimatorCondition GenerateEqualsCondition(string parameter, int threshold, bool invert = false) {
        return new AnimatorCondition() {
            mode = invert ? AnimatorConditionMode.NotEqual : AnimatorConditionMode.Equals,
            parameter = parameter,
            threshold = (float)threshold,
        };
    }

    private IEnumerable<AnimatorCondition> GenerateDrawingConditions(string parameter, bool isExit = false) {
        var creator = target as SnailMarkerAnimationCreator;

        var conditions = new List<AnimatorCondition> {
            GenerateEqualsCondition(parameter, 1, isExit),
        };
        if (creator.useGestureLeft) {
            conditions.Add(GenerateEqualsCondition("GestureLeft", (int)creator.gestureLeft, isExit));
        }
        if (creator.useGestureRight) {
            conditions.Add(GenerateEqualsCondition("GestureRight", (int)creator.gestureRight, isExit));
        }

        return conditions;
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

        if (!controller.parameters.Any(p => p.name == "GestureLeft")) {
            controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
        }
        if (!controller.parameters.Any(p => p.name == "GestureRight")) {
            controller.AddParameter("GestureRight", AnimatorControllerParameterType.Int);
        }

        var layerName = controller.MakeUniqueLayerName("Marker");

        controller.AddLayer(layerName);
        var markerLayer = controller.layers.First(l => l.name == layerName);
        markerLayer.defaultWeight = 1.0f;

        var waitState = AddState(markerLayer.stateMachine, "Wait");
        markerLayer.stateMachine.defaultState = waitState;

        AddState(
            markerLayer.stateMachine,
            "Drawing",
            AssetDatabase.LoadAssetAtPath<AnimationClip>(exportPath + "Drawing.anim"),
            GenerateDrawingConditions(parameterName, false),
            GenerateDrawingConditions(parameterName, true)
        );
        AddState(
            markerLayer.stateMachine,
            "Erase All",
            AssetDatabase.LoadAssetAtPath<AnimationClip>(exportPath + "EraseAll.anim"),
            new [] { GenerateEqualsCondition(parameterName, 2) },
            new [] { GenerateEqualsCondition(parameterName, 2, true) }
        );

        EditorUtility.SetDirty(controller);

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
            icon = FindTexture("MarkerDrawing"),
        });
        markerMenu.controls.Add(new VRCExpressionsMenu.Control() {
            name = "Erase All",
            type = VRCExpressionsMenu.Control.ControlType.Button,
            parameter = new VRCExpressionsMenu.Control.Parameter() {
                name = parameterName,
            },
            value = 2,
            icon = FindTexture("MarkerEraseAll"),
        });

        WriteAsset(markerMenu, exportPath + "MarkerMenu.asset");

        var menu = descriptor.expressionsMenu;
        if (menu == null) {
            descriptor.expressionsMenu = markerMenu;
        } else {
            menu.controls.Add(new VRCExpressionsMenu.Control() {
                name = "Marker",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = markerMenu,
                icon = FindTexture("MarkerDrawing"),
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

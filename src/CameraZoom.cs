﻿using HarmonyLib;
using MGSC;
using System;
using System.Data.SqlTypes;
using System.Reflection;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using static MGSC.Localization;

namespace QM_CameraPanAndZoom
{
    internal class CameraZoom
    {
        private static DungeonGameMode dungeonGameMode = null;
        private static GameCamera gameCamera = null;
        private static Camera camera = null;

        private static class CursorState
        {
            public static Vector3 MouseWorldPosBefore { get; set; }
            public static Vector3 MouseWorldPosAfter { get; set; }
        }

        private static class ModConfiguration
        {
            public static bool IsInitialized { get; set; } = false;
            public static bool ModZoomTweakEnabled { get; set; } = false;
            public static bool ModPanningEnabled { get; set; } = false;
        }

        private static class CameraConfiguration
        {
            public static bool CameraNeedMoving { get; set; } = false;
            public static Vector3 StoredCameraPosition { get; set; }
            public const float CAMERA_STOPPED_THRESHOLD = 0.01f; // 100ms - time to wait if camera position doesn't change
            public const float CAMERA_POSITION_TOLERANCE = 0.01f; // Small tolerance for position comparison
            public static float CameraMoveSpeed { get; set; } = 0.05f; // Speed of camera movement (0.25f is default)
        }

        private static class CameraState
        {
            public static float LastCameraPositionChangeTime { get; set; } = 0f;
            public static int LastZoom { get; set; } = -1;
            public static bool CooldownInProgress { get; set; }
        }

        private static class ZoomConfiguration
        {
            public static bool AlternativeMode { get; set; } = false;
            public static int PpuStep { get; set; } = 6;
            public static float Duration { get; set; } = 0.05f; // Duration of zoom animation in seconds
            public static int PpuDefault { get; set; } = 64; // ZoomConfiguration.PpuDefault must be default for new default zoom
            public static float PpuMin { get; set; } = 5f;
            public static float PpuMax { get; set; } = 400f;
            public const float CURVE_ACCELERATION_FACTOR = 1.8f;
            public const float MIN_MOVEMENT_THRESHOLD = 0.1f;
            public const float ZOOM_COOLDOWN = 0.01f; // Minimum time between zoom operations (ms)
        }

        private static class ZoomState
        {
            public static float OldPPU { get; set; } = 0f;
            public static float NewPPU { get; set; } = 0f;
            public static bool IsZooming { get; set; } = false;
            public static float ZoomStartTime { get; set; } = 0f;
            public static float LastZoomTime { get; set; } = 0f;
        }

        private static class PanState
        {
            public static bool IsPanning { get; set; }
            public static Vector3 LastMousePosition { get; set; }
            public static float Sensitivity { get; set; } = 1.0f; // Adjustable pan sensitivity multiplier (1.0 = normal, 2.0 = double speed, 0.5 = half speed)
        }

        private static class ZoomCalculationConstants
        {
            public const float MOVEMENT_THRESHOLD = 0.1f;
            public const float PROGRESS_MIDPOINT = 0.5f;
            public const float PROGRESS_MIDPOINT_FAST = 2f;
            public const float EXPONENTIAL_GROWTH_FACTOR = 3f;
            public const float STEP_REDUCTION_FACTOR = 0.7f;
            public const float MIN_STEP_MULTIPLIER = 0.2f;
        }

        [Hook(ModHookType.MainMenuStarted)]
        public static void MainMenuStarted(IModContext context)
        {
            Plugin.Logger.Log("MainMenuStarted");
        }

        [Hook(ModHookType.SpaceStarted)]
        public static void SpaceStarted(IModContext context)
        {
            Plugin.Logger.Log("SpaceStarted");
        }

        [Hook(ModHookType.DungeonStarted)]
        public static void DungeonStarted(IModContext context)
        {
            Plugin.Logger.Log("DungeonStarted");
        }

        [Hook(ModHookType.DungeonFinished)]
        public static void DungeonFinished(IModContext context)
        {
            ModConfiguration.ModZoomTweakEnabled = false;
            ModConfiguration.ModPanningEnabled = false;
            ModConfiguration.IsInitialized = false;
            dungeonGameMode = null;
            GameCamera._lastZoom = -1; // We need to reset last zoom as new camera will not have our new array.
        }

        [HarmonyPatch(typeof(GameCamera), "ZoomIn")]
        public static class ZoomIn_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                Plugin.Logger.Log("ZoomIn");
                return HandleZoom(isZoomIn: true);
            }

        }

        [HarmonyPatch(typeof(GameCamera), "ZoomOut")]
        public static class ZoomOut_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                Plugin.Logger.Log("ZoomOut");
                return HandleZoom(isZoomIn: false);
            }
        }

        private static bool HandleAlternativeZoom(bool isZoomIn)
        {
            // Get current PPU as old value
            ZoomState.OldPPU = dungeonGameMode.GameCamera._pixelPerfectCamera.assetsPPU;

            // Calculate dynamic step size based on current PPU
            int dynamicStep = CalculateDynamicStep(ZoomState.OldPPU, isZoomIn); // true for zoom in

            // Calculate new PPU with bounds checking
            if (isZoomIn)
            {
                ZoomState.NewPPU = Mathf.Clamp(ZoomState.OldPPU + dynamicStep, ZoomConfiguration.PpuMin, ZoomConfiguration.PpuMax);
            }
            else
            {
                ZoomState.NewPPU = Mathf.Clamp(ZoomState.OldPPU - dynamicStep, ZoomConfiguration.PpuMin, ZoomConfiguration.PpuMax);
            }

            Plugin.Logger.Log($"newPPU PPU: {ZoomState.NewPPU}");

            CameraConfiguration.CameraNeedMoving = true;
            ZoomState.LastZoomTime = Time.time;
            return false;
        }

        private static void HandleIndexBasedZoom(bool isZoomIn)
        {
            int newZoomIndex = 0;
            if (isZoomIn)
            {
                // Calculate new zoom index first
                newZoomIndex = dungeonGameMode.GameCamera._currentZoomIndex - 1;
                if (newZoomIndex < 0)
                {
                    newZoomIndex = 0;
                }
            }
            else
            {
                // Calculate new zoom index first
                newZoomIndex = dungeonGameMode.GameCamera._currentZoomIndex + 1;
                if (newZoomIndex >= dungeonGameMode.GameCamera._zoomLevels.Length)
                {
                    newZoomIndex = dungeonGameMode.GameCamera._zoomLevels.Length - 1;
                }
            }

            ApplyNewZoomIndex(newZoomIndex);
        }

        private static bool HandleZoom(bool isZoomIn)
        {
            if (!ModConfiguration.ModZoomTweakEnabled)
            {
                return true; // Skip and use original method.
            }

            if (CameraConfiguration.CameraNeedMoving || CameraState.CooldownInProgress || ZoomState.IsZooming)
            {
                return false; // While we are handling camera movement or zooming, do nothing.
            }

            if (ZoomConfiguration.AlternativeMode)
            {
                HandleAlternativeZoom(isZoomIn);
            }
            else
            {
                HandleIndexBasedZoom(isZoomIn);
            }

            return false;
        }

        private static void ApplyNewZoomIndex(int newZoomIndex)
        {
            // Get current PPU as old value
            ZoomState.OldPPU = dungeonGameMode.GameCamera._pixelPerfectCamera.assetsPPU;
            ZoomState.NewPPU = gameCamera._zoomLevels[newZoomIndex];

            CameraConfiguration.CameraNeedMoving = true;
            ZoomState.LastZoomTime = Time.time;

            // Apply zoom index change
            dungeonGameMode.GameCamera._currentZoomIndex = newZoomIndex;
            GameCamera._lastZoom = dungeonGameMode.GameCamera._currentZoomIndex;
        }

        [Hook(ModHookType.DungeonUpdateBeforeGameLoop)]
        public static void DungeonUpdateBeforeGameLoop(IModContext context)
        {
            ModConfiguration.ModZoomTweakEnabled = Plugin.Config.ZoomTweakEnabled;
            ModConfiguration.ModPanningEnabled = Plugin.Config.PanningEnabled;

            if (ModConfiguration.ModPanningEnabled)
            {
                HandleCameraPanning();
            }

            if (!ModConfiguration.ModZoomTweakEnabled)
            {
                return;
            }
            if (CameraState.LastZoom != GameCamera._lastZoom)
            {
                if (CameraState.LastZoom > GameCamera._lastZoom)
                {
                    // Zoom in
                    //cameraNeedMoving = true;
                }

                CameraState.LastZoom = GameCamera._lastZoom;
            }

            HandleCameraMovement();
            Initialize();
        }

        private static int CalculateDynamicStep(float currentPPU, bool isZoomingIn)
        {
            // Calculate distance from default (64)
            float distanceFromDefault = Mathf.Abs(currentPPU - ZoomConfiguration.PpuDefault);

            // Calculate step multiplier based on current PPU value
            float stepMultiplier = 1f;

            if (isZoomingIn)
            {
                // For zoom in: larger PPU values get bigger steps
                // Use exponential scaling for higher zoom levels
                if (currentPPU > ZoomConfiguration.PpuDefault)
                {
                    // Above default: increase step size exponentially
                    float ratio = (currentPPU - ZoomConfiguration.PpuDefault) / (ZoomConfiguration.PpuMax - ZoomConfiguration.PpuDefault);
                    stepMultiplier = 1f + (ratio * ratio * ZoomCalculationConstants.EXPONENTIAL_GROWTH_FACTOR); // Exponential growth
                }
                else
                {
                    // Below default: smaller steps to approach default smoothly
                    float ratio = (ZoomConfiguration.PpuDefault - currentPPU) / (ZoomConfiguration.PpuDefault - ZoomConfiguration.PpuMin);
                    stepMultiplier = 0.5f + (ratio * ZoomCalculationConstants.PROGRESS_MIDPOINT); // Gradual approach to default
                }
            }
            else
            {
                // For zoom out: smaller PPU values get smaller steps
                if (currentPPU < ZoomConfiguration.PpuDefault)
                {
                    // Below default: decrease step size as we get further from default
                    float ratio = (ZoomConfiguration.PpuDefault - currentPPU) / (ZoomConfiguration.PpuDefault - ZoomConfiguration.PpuMin);
                    stepMultiplier = 1f - (ratio * ZoomCalculationConstants.STEP_REDUCTION_FACTOR); // Smaller steps as we go further
                    stepMultiplier = Mathf.Max(stepMultiplier, ZoomCalculationConstants.MIN_STEP_MULTIPLIER); // Minimum step multiplier
                }
                else
                {
                    // Above default: larger steps to approach default faster
                    float ratio = (currentPPU - ZoomConfiguration.PpuDefault) / (ZoomConfiguration.PpuMax - ZoomConfiguration.PpuDefault);
                    stepMultiplier = 1f + (ratio * ZoomCalculationConstants.PROGRESS_MIDPOINT_FAST); // Faster approach to default
                }
            }

            // Calculate final step size
            int dynamicStep = Mathf.RoundToInt(ZoomConfiguration.PpuStep * stepMultiplier);

            // Ensure minimum step size of 1
            dynamicStep = Mathf.Max(1, dynamicStep);

            // Add bias towards default value (64)
            if (isZoomingIn && currentPPU < ZoomConfiguration.PpuDefault)
            {
                // When zooming in and below default, add extra step to reach default faster
                float biasToDefault = (ZoomConfiguration.PpuDefault - currentPPU) / ZoomConfiguration.PpuDefault;
                dynamicStep += Mathf.RoundToInt(biasToDefault * ZoomConfiguration.PpuStep * 0.5f);
            }
            else if (!isZoomingIn && currentPPU > ZoomConfiguration.PpuDefault)
            {
                // When zooming out and above default, add extra step to reach default faster
                float biasToDefault = (currentPPU - ZoomConfiguration.PpuDefault) / ZoomConfiguration.PpuDefault;
                dynamicStep += Mathf.RoundToInt(biasToDefault * ZoomConfiguration.PpuStep * 0.5f);
            }

            Plugin.Logger.Log($"Dynamic step calculation - Current PPU: {currentPPU}, Is Zoom In: {isZoomingIn}, Step Multiplier: {stepMultiplier:F2}, Final Step: {dynamicStep}");

            return dynamicStep;
        }

        private static void CreateZoomLevelsArray()
        {
            //_defaultZoomIndex = gameCamera._defaultZoomIndex;
            //_zoomLevels = gameCamera._zoomLevels; // default 6
            //_lastZoom = 3;

            // When dungeon starts it remembers last settings if dungeon was started before
            // Fresh game launch settings as follows:
            // _defaultZoomIndex 3
            // _currentZoomIndex 3
            // _lastZoom 3

            // GameCamera._pixelPerfectCamera
            // UnityEngine.Experimental.Rendering.Universal.PixelPerfectCamera
            // PixelPerfectCamera.m_AssetsPPU 64 // default zoom 3 idk why didnt check
            // PixelPerfectCamera.pixelRatio 6

            // _zoomLevels 6
            // _zoomLevels level 78 ? max zoom in 
            // _zoomLevels level 76
            // _zoomLevels level 70
            // _zoomLevels level 64
            // _zoomLevels level 58
            // _zoomLevels level 50 ? max zoom out

            // zoomin: index--
            // zoomout: index++


            //var fieldInfo = typeof(GameCamera).GetField("_zoomLevels", BindingFlags.NonPublic | BindingFlags.Instance);
            //int[] array = (int[])fieldInfo.GetValue(gameCamera);
            //int[] newArray = new int[11];
            //Array.Copy(array, newArray, 6);
            //int lastZoomLevel = array[5];
            //int j = 5;
            //for (int i = 1; i <= 5; i++)
            //{
            //    newArray[j + i] = lastZoomLevel - (i * 8);
            //}
            //fieldInfo.SetValue(gameCamera, newArray);

            var newArray = new int[Plugin.Config.ZoomOutSteps + Plugin.Config.ZoomInSteps + 1]; // 5 + 5 = 1 = 11
            Plugin.Logger.Log($"newArray with len: {newArray.Length}");

            // Find centralIndex and if length is even, subtract 1 to make it odd
            var centralIndex = newArray.Length / 2;
            if (centralIndex % 2 == 0)
            {
                centralIndex -= 1;
            }

            newArray[centralIndex] = ZoomConfiguration.PpuDefault;
            Plugin.Logger.Log($"centralIndex: {centralIndex}");

            FillZoomLevelsArray(newArray, centralIndex);
            UpdateCameraZoomProperties(newArray, centralIndex);
        }

        private static void UpdateCameraZoomProperties(int[] newArray, int centralIndex)
        {
            Plugin.Logger.Log($"Updating gamecamera zoom vars");

            gameCamera._zoomLevels = newArray;
            Plugin.Logger.Log($"Updated: gameCamera._zoomLevels");

            gameCamera._defaultZoomIndex = centralIndex;
            Plugin.Logger.Log($"Updated: gameCamera._defaultZoomIndex");

            gameCamera._currentZoomIndex = centralIndex;
            Plugin.Logger.Log($"Updated: gameCamera._currentZoomIndex");

            if (CameraState.LastZoom > 0) // We need to update our camera last zoom
            {
                Plugin.Logger.Log($"_lastZoom > 0)");

                if (CameraState.LastZoom < gameCamera._zoomLevels.Length) // We within of array bounds
                {
                    Plugin.Logger.Log($"_lastZoom < gameCamera._zoomLevels.Length");
                    CameraState.LastZoom = gameCamera._defaultZoomIndex;
                    GameCamera._lastZoom = gameCamera._defaultZoomIndex;
                }
                else
                {
                    Plugin.Logger.Log($"_lastZoom >= gameCamera._zoomLevels.Length");
                    CameraState.LastZoom = -1;
                }

                if (GameCamera._lastZoom > 0) // We got some camera zoom saved
                {
                    Plugin.Logger.Log($"GameCamera._lastZoom > 0");
                    CameraState.LastZoom = GameCamera._lastZoom;
                }
                else
                {
                    Plugin.Logger.Log($"GameCamera._lastZoom > 0 ELSE");
                    // Set new lastzoom and currend zoom

                }
            }

            Plugin.Logger.Log($"_lastZoom {CameraState.LastZoom}");
        }

        private static void FillZoomLevelsArray(int[] newArray, int centralIndex)
        {
            // Zoom In - gradually increasing increments (bigger zoom = bigger increase)
            // Zoom In - with minimum step size to avoid slowdown in middle
            var zoomInRange = ZoomConfiguration.PpuMax - ZoomConfiguration.PpuDefault;
            var minStepSize = 4; // Minimum step to avoid slowdown
            for (int i = centralIndex - 1, step = 1; i >= 0; i--, step++)
            {
                var progressRatio = (float)step / Plugin.Config.ZoomInSteps;

                // Use a curve that provides consistent middle steps but accelerates at extremes
                var curveValue = progressRatio < 0.5f
                    ? 0.5f + progressRatio // Linear growth in first half
                    : 0.5f + progressRatio + (progressRatio - 0.5f) * progressRatio; // Accelerated growth in second half

                var increment = Math.Max(minStepSize, (int)(zoomInRange * curveValue / Plugin.Config.ZoomInSteps * ZoomConfiguration.CURVE_ACCELERATION_FACTOR));

                var newValue = newArray[i + 1] + increment;
                newArray[i] = Math.Min((int)ZoomConfiguration.PpuMax, newValue);
            }

            // Zoom Out - gradually decreasing decrements (making it smaller)
            // Zoom Out - with minimum step size to avoid slowdown in middle
            var zoomOutRange = ZoomConfiguration.PpuDefault - ZoomConfiguration.PpuMin;
            for (int i = centralIndex + 1, step = 1; i < newArray.Length; i++, step++)
            {
                var progressRatio = (float)step / Plugin.Config.ZoomOutSteps;

                // Similar curve for zoom out - consistent middle steps, more aggressive at extremes
                var curveValue = progressRatio < 0.5f
                    ? 0.5f + progressRatio // Linear decrease in first half
                    : 0.5f + progressRatio + (progressRatio - 0.5f) * progressRatio; // Accelerated decrease in second half

                var decrement = Math.Max(minStepSize, (int)(zoomOutRange * curveValue / Plugin.Config.ZoomOutSteps * ZoomConfiguration.CURVE_ACCELERATION_FACTOR));

                var newValue = newArray[i - 1] - decrement;
                newArray[i] = Math.Max((int)ZoomConfiguration.PpuMin, newValue);
            }

            for (int i = 0; i < newArray.Length; i++)
            {
                Plugin.Logger.Log($"newArray zoomLevels level {newArray[i]}");
            }
        }

        private static void Initialize()
        {
            if (!ModConfiguration.IsInitialized)
            {
                try
                {
                    FindGameObjects();
                    LoadConfiguration();

                    if (ModConfiguration.IsInitialized)
                    {
                        return;
                    }

                    CreateZoomLevelsArray();
                    UpdateCameraZoomPPU();
                    ModConfiguration.IsInitialized = true;
                    Plugin.Logger.Log("Initialized");

                }
                catch (Exception ex)
                {
                    Plugin.Logger.Log($"Error in initialization: {ex.Message}");
                    Plugin.Logger.Log($"{ex.StackTrace}");
                }
            }
        }

        private static void LoadConfiguration()
        {
            CameraConfiguration.CameraMoveSpeed = Plugin.Config.CameraMoveDuration / 100f;
            ZoomConfiguration.Duration = Plugin.Config.ZoomDuration / 100f;
            PanState.Sensitivity = Plugin.Config.PanSensitivity;
            ZoomConfiguration.AlternativeMode = Plugin.Config.ZoomAlternativeMode;
            ZoomConfiguration.PpuMin = Plugin.Config.ZoomMin;
            ZoomConfiguration.PpuMax = Plugin.Config.ZoomMax;

            if (ZoomConfiguration.AlternativeMode)
            {
                Plugin.Logger.Log($"Using alternative zoom");
                CameraConfiguration.CameraMoveSpeed = 0f;
                ZoomConfiguration.Duration = 0f;
                UpdateCameraZoomPPU();
                ModConfiguration.IsInitialized = true;
            }
        }

        private static void UpdateCameraZoomPPU()
        {
            if (ZoomConfiguration.AlternativeMode)
            {
                gameCamera._pixelPerfectCamera.assetsPPU = (int)(ZoomState.NewPPU == 0 ? ZoomConfiguration.PpuDefault : ZoomState.NewPPU);
            }
            else
            {
                // Update our zoom PPU
                Plugin.Logger.Log($"updatnig pixelPerfectCamera.assetsPPU {gameCamera._zoomLevels[GameCamera._lastZoom]}");
                gameCamera._pixelPerfectCamera.assetsPPU = gameCamera._zoomLevels[GameCamera._lastZoom];
            }
        }

        private static void FindGameObjects()
        {
            dungeonGameMode = GameObject.FindObjectOfType<DungeonGameMode>(true);
            gameCamera = dungeonGameMode._camera;
            camera = dungeonGameMode._camera.GetComponent<Camera>();
        }

        private static void HandleCameraMovement()
        {
            if (PanState.IsPanning)
            {
                return;
            }

            HandleSmoothZooming();
            HandleCameraPositioning();
            HandleMovementCooldown();
        }

        private static void HandleMovementCooldown()
        {
            if (CameraState.CooldownInProgress)
            {
                // Check current camera position after movement command
                Vector3 currentPos = dungeonGameMode._camera.transform.position;

                // Check if camera position has changed since last frame
                if (Vector3.Distance(currentPos, CameraConfiguration.StoredCameraPosition) > CameraConfiguration.CAMERA_POSITION_TOLERANCE)
                {
                    // Camera is still moving, update stored position and time
                    CameraConfiguration.StoredCameraPosition = currentPos;
                    CameraState.LastCameraPositionChangeTime = Time.time;
                    Plugin.Logger.Log($"Camera still moving - Current pos: {currentPos}");
                }
                else
                {
                    Plugin.Logger.Log($"Camera not moving - Current pos: {currentPos}");
                }

                // Check if we can stop camera movement handling
                bool cooldownComplete = Time.time - ZoomState.LastZoomTime > ZoomConfiguration.ZOOM_COOLDOWN;
                bool cameraStoppedMoving = Time.time - CameraState.LastCameraPositionChangeTime > CameraConfiguration.CAMERA_STOPPED_THRESHOLD;
                bool zoomComplete = !ZoomState.IsZooming; // Also wait for zoom to complete

                if (cooldownComplete && cameraStoppedMoving && zoomComplete)
                {
                    Plugin.Logger.Log($"Camera movement complete - Cooldown: {cooldownComplete}, Camera stopped: {cameraStoppedMoving}, Zoom complete: {zoomComplete}");
                    CameraState.CooldownInProgress = false;
                }
            }
        }

        private static void HandleCameraPositioning()
        {
            if (CameraConfiguration.CameraNeedMoving && dungeonGameMode != null)
            {
                // Store current camera position before moving
                Vector3 currentCameraPos = gameCamera.transform.position;
                CameraConfiguration.StoredCameraPosition = currentCameraPos;
                CameraState.LastCameraPositionChangeTime = Time.time;

                CursorState.MouseWorldPosBefore = MouseScreenToWorldPoint();

                // Predict where mouse will be after zoom
                CursorState.MouseWorldPosAfter = ZoomConfiguration.AlternativeMode ? PredictMouseWorldPositionAfterZoom(ZoomState.NewPPU) : PredictMouseWorldPositionAfterZoom(gameCamera._zoomLevels[gameCamera._currentZoomIndex]);

                // Calculate the difference - this is how much the world point under cursor will shift
                Vector3 worldShift = CursorState.MouseWorldPosAfter - CursorState.MouseWorldPosBefore;

                // Move camera by the opposite of this shift to keep the same world point under cursor
                Vector3 targetCameraPos = currentCameraPos - worldShift;

                Plugin.Logger.Log($"Predicted mouse world pos after zoom: {CursorState.MouseWorldPosAfter}");
                Plugin.Logger.Log($"Mouse world pos before zoom: {CursorState.MouseWorldPosBefore}");
                Plugin.Logger.Log($"World shift: {worldShift}");
                Plugin.Logger.Log($"Target camera pos: {targetCameraPos}");

                // Move camera to compensate for the zoom shift
                gameCamera.MoveCameraToPosition(targetCameraPos, CameraConfiguration.CameraMoveSpeed);

                // Set camera mode if needed
                gameCamera.SetCameraMode(CameraMode.BorderMove);
                CameraConfiguration.CameraNeedMoving = false;
                CameraState.CooldownInProgress = true;

                // Start smooth zoom transition
                ZoomState.IsZooming = CanSmoothZoom();
            }
        }

        private static void HandleSmoothZooming()
        {
            // Handle smooth zooming
            if (ZoomState.IsZooming)
            {
                float elapsedTime = Time.time - ZoomState.ZoomStartTime;
                float progress = Mathf.Clamp01(elapsedTime / ZoomConfiguration.Duration);

                // Use smooth curve for zoom transition
                float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

                // Interpolate PPU value
                float temporaryPPU = Mathf.Lerp(ZoomState.OldPPU, ZoomState.NewPPU, smoothProgress);
                gameCamera._pixelPerfectCamera.assetsPPU = (int)temporaryPPU;

                // Check if zoom animation is complete
                if (progress >= 1f)
                {
                    // Ensure final PPU is exactly the target value
                    gameCamera._pixelPerfectCamera.assetsPPU = (int)ZoomState.NewPPU;
                    ZoomState.IsZooming = false;
                    Plugin.Logger.Log($"Smooth zoom complete: {ZoomState.OldPPU} -> {ZoomState.NewPPU}");
                }

                return; // Don't process camera movement while zooming
            }
        }

        private static Vector3 MouseScreenToWorldPoint()
        {
            // Get mouse position in screen coordinates at current zoom level
            Vector3 mouseScreenPos = Input.mousePosition;

            // Convert mouse screen position to world coordinates at current zoom level
            return camera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, camera.nearClipPlane));
        }

        private static bool CanSmoothZoom()
        {
            // Only start smooth zoom if there's a difference
            if (Mathf.Abs(ZoomState.OldPPU - ZoomState.NewPPU) > ZoomConfiguration.MIN_MOVEMENT_THRESHOLD)
            {
                ZoomState.ZoomStartTime = Time.time;
                Plugin.Logger.Log($"Starting smooth zoom: {ZoomState.OldPPU} -> {ZoomState.NewPPU}");
                return true;
            }
            else
            {
                // No significant change, just set the value directly
                gameCamera._pixelPerfectCamera.assetsPPU = (int)ZoomState.NewPPU;
                Plugin.Logger.Log($"Starting smooth zoom: No significant change, setting the value directly {ZoomState.OldPPU} -> {ZoomState.NewPPU}");
                return false;
            }
        }

        private static void HandleCameraPanning()
        {
            if (dungeonGameMode == null) return;
            if (gameCamera == null) return;

            // Check for pan input based on config setting
            bool panInputPressed = IsPanButtonPressed();

            if (panInputPressed)
            {
                if (!PanState.IsPanning)
                {
                    // Start panning
                    PanState.IsPanning = true;
                    PanState.LastMousePosition = Input.mousePosition;
                    Plugin.Logger.Log("Started camera panning");
                }
                else
                {
                    // Continue panning
                    Vector3 currentMousePosition = Input.mousePosition;
                    Vector3 mouseDelta = currentMousePosition - PanState.LastMousePosition;

                    if (mouseDelta.magnitude > 0.1f) // Only pan if there's meaningful movement
                    {
                        // Convert mouse delta to world space movement
                        Camera camera = gameCamera.Camera;

                        // Calculate world space movement based on current zoom level
                        float worldUnitsPerPixel = camera.orthographicSize * 2f / Screen.height;
                        Vector3 worldDelta = new Vector3(-mouseDelta.x * worldUnitsPerPixel * PanState.Sensitivity,
                                                        -mouseDelta.y * worldUnitsPerPixel * PanState.Sensitivity,
                                                        0);

                        // Apply the movement to camera
                        Vector3 currentCameraPos = gameCamera.transform.position;
                        Vector3 targetCameraPos = currentCameraPos + worldDelta;

                        Plugin.Logger.Log($"Panning camera from {currentCameraPos} to {targetCameraPos} (delta: {worldDelta}, sensitivity: {PanState.Sensitivity})");

                        // Move camera immediately for responsive panning
                        gameCamera.MoveCameraToPosition(targetCameraPos, 0f);

                        // Set camera mode for panning
                        gameCamera.SetCameraMode(CameraMode.BorderMove);
                    }

                    PanState.LastMousePosition = currentMousePosition;
                }
            }
            else
            {
                if (PanState.IsPanning)
                {
                    // Stop panning
                    PanState.IsPanning = false;
                    Plugin.Logger.Log("Stopped camera panning");
                }
            }
        }


        private static Vector3 PredictMouseWorldPositionAfterZoom(float newPPU)
        {
            if (dungeonGameMode == null) return Vector3.zero;

            // Get current mouse screen position
            Vector3 mouseScreenPos = Input.mousePosition;

            // Calculate the scale factor between old and new zoom
            float zoomScaleFactor = gameCamera._pixelPerfectCamera.assetsPPU / ZoomState.NewPPU;

            // Get current camera orthographic size
            float currentOrthoSize = camera.orthographicSize;

            // Calculate what the new orthographic size will be
            float newOrthoSize = currentOrthoSize * zoomScaleFactor;

            // Convert mouse screen position to world coordinates using predicted orthographic size
            float halfHeight = newOrthoSize;
            float halfWidth = halfHeight * camera.aspect;

            // Convert screen coordinates to normalized coordinates (0-1)
            float normalizedX = mouseScreenPos.x / Screen.width;
            float normalizedY = mouseScreenPos.y / Screen.height;

            // Convert to world coordinates relative to camera position
            Vector3 cameraPos = camera.transform.position;
            float worldX = cameraPos.x + (normalizedX - 0.5f) * 2f * halfWidth;
            float worldY = cameraPos.y + (normalizedY - 0.5f) * 2f * halfHeight;

            return new Vector3(worldX, worldY, camera.nearClipPlane);
        }

        private static bool IsPanButtonPressed()
        {
            // Check each Unity mouse button (0-4) to see if it's pressed
            for (int unityButton = 0; unityButton <= 4; unityButton++)
            {
                if (Input.GetMouseButton(unityButton))
                {
                    // Convert Unity button index to Mod enum value (add 1)
                    MouseButtonsMod modButton = (MouseButtonsMod)(unityButton + 1);

                    // Compare with config setting
                    if ((int)modButton == Plugin.Config.PanButton)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public enum MouseButtonsUnity
    {
        Left,
        Right,
        Middle,
        Back,
        Forward
    }

    public enum MouseButtonsMod
    {
        Left = 1,
        Right,
        Middle,
        Back,
        Forward
    }

}

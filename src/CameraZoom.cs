using HarmonyLib;
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

namespace QM_CameraZoomTweaker
{
    internal class CameraZoom
    {
        private static bool isInitialized = false;
        private static bool enabled = false;
        private static int[] newZoomArray;
        private static DungeonGameMode dungeonGameMode = null;
        // GameCamera
        private static int _lastZoom = -1;
        private static bool cameraNeedMoving;
        private static bool cooldownInProgress;
        private static CellPosition cellPosition;
        private static Vector3 mouseWorldPosBefore;
        private static Vector3 mouseWorldPosAfter;

        private static float lastZoomTime = 0f;
        private static float zoomCooldown = 0.05f; // Minimum time between zoom operations (ms)

        private static Vector3 storedCameraPosition;
        private static float lastCameraPositionChangeTime = 0f;
        private static float cameraStoppedThreshold = 0.1f; // 100ms - time to wait if camera position doesn't change
        private static float cameraPositionTolerance = 0.01f; // Small tolerance for position comparison
        private static float cameraMoveSpeed = 0.05f; // Speed of camera movement (0.25f is default)

        private static bool isPanning = false;
        private static Vector3 lastPanMousePosition;
        private static float panSensitivity = 1.0f; // Adjust this value to control pan speed

        private static bool isZooming = false;
        private static float zoomStartTime = 0f;
        private static int oldZoomIndex = -1;
        private static int newZoomIndex = -1;
        private static float oldPPU = 0f;
        private static float newPPU = 0f;
        private static float zoomDuration = 0.05f; // Duration of zoom animation in seconds

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
            enabled = false;
            isInitialized = false;
            dungeonGameMode = null;
            GameCamera._lastZoom = -1; // We need to reset last zoom as new camera will not have our new array.
        }

        [HarmonyPatch(typeof(GameCamera), "ZoomIn")]
        public static class ZoomIn_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (cameraNeedMoving || cooldownInProgress || isZooming)
                {
                    return false; // While we are handling camera movement or zooming, ignore the original method.
                }

                if (dungeonGameMode != null)
                {
                    // Calculate new zoom index first
                    int newZoomIndex = dungeonGameMode.GameCamera._currentZoomIndex - 1;
                    if (newZoomIndex < 0)
                    {
                        newZoomIndex = 0;
                    }

                    // Get mouse position in screen coordinates at current zoom level
                    Vector3 mouseScreenPos = Input.mousePosition;

                     // Get current PPU as old value
                    oldPPU = dungeonGameMode.GameCamera._pixelPerfectCamera.assetsPPU;

                    // Convert mouse screen position to world coordinates at current zoom level
                    Camera camera = dungeonGameMode._camera.Camera;
                    mouseWorldPosBefore = camera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, camera.nearClipPlane));

                    // Predict where mouse will be after zoom
                    mouseWorldPosAfter = PredictMouseWorldPositionAfterZoom(newZoomIndex);

                    cameraNeedMoving = true;
                    lastZoomTime = Time.time;

                    // Apply zoom index change
                    dungeonGameMode.GameCamera._currentZoomIndex = newZoomIndex;
                    GameCamera._lastZoom = dungeonGameMode.GameCamera._currentZoomIndex;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "ZoomOut")]
        public static class ZoomOut_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (cameraNeedMoving || cooldownInProgress || isZooming)
                {
                    return false;
                }

                if (dungeonGameMode != null)
                {
                    // Calculate new zoom index first
                    int newZoomIndex = dungeonGameMode.GameCamera._currentZoomIndex + 1;
                    if (newZoomIndex >= dungeonGameMode.GameCamera._zoomLevels.Length)
                    {
                        newZoomIndex = dungeonGameMode.GameCamera._zoomLevels.Length - 1;
                    }

                    // Get mouse position in screen coordinates at current zoom level
                    Vector3 mouseScreenPos = Input.mousePosition;

                    // Get current PPU as old value
                    oldPPU = dungeonGameMode.GameCamera._pixelPerfectCamera.assetsPPU;

                    // Convert mouse screen position to world coordinates at current zoom level
                    Camera camera = dungeonGameMode._camera.Camera;
                    mouseWorldPosBefore = camera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, camera.nearClipPlane));

                    // Predict where mouse will be after zoom
                    mouseWorldPosAfter = PredictMouseWorldPositionAfterZoom(newZoomIndex);

                    cameraNeedMoving = true;
                    lastZoomTime = Time.time;

                    // Apply zoom index change
                    dungeonGameMode.GameCamera._currentZoomIndex = newZoomIndex;
                    GameCamera._lastZoom = dungeonGameMode.GameCamera._currentZoomIndex;
                }

                return false;
            }
        }

        [Hook(ModHookType.DungeonUpdateBeforeGameLoop)]
        public static void DungeonUpdateBeforeGameLoop(IModContext context)
        {
            enabled = Plugin.Config.ModEnabled;

            if (_lastZoom != GameCamera._lastZoom)
            {
                if (_lastZoom > GameCamera._lastZoom)
                {
                    // Zoom in
                    //cameraNeedMoving = true;
                }

                _lastZoom = GameCamera._lastZoom;
            }

            HandleCameraPanning();
            HandleCameraMovement();

            if (!isInitialized)
            {
                try
                {
                    dungeonGameMode = GameObject.FindObjectOfType<DungeonGameMode>(true);
                    var gameCamera = dungeonGameMode._camera;
                    var camera = dungeonGameMode._camera.GetComponent<Camera>();
                    //var gameCamera = GameObject.FindObjectOfType<GameCamera>();

                    if (gameCamera != null)
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

                        // ppu must be default for new default zoom
                        var ppu = 64;
                        var ppuMin = 5f;
                        var ppuMax = 400f;

                        // Find centralIndex and if length is even, subtract 1 to make it odd
                        var centralIndex = newArray.Length / 2;
                        if (centralIndex % 2 == 0)
                        {
                            centralIndex -= 1;
                        }

                        newArray[centralIndex] = ppu;
                        Plugin.Logger.Log($"centralIndex: {centralIndex}");

                        // Zoom In - gradually increasing increments (bigger zoom = bigger increase)
                        // Zoom In - with minimum step size to avoid slowdown in middle
                        var zoomInRange = ppuMax - ppu;
                        var minStepSize = 4; // Minimum step to avoid slowdown
                        for (int i = centralIndex - 1, step = 1; i >= 0; i--, step++)
                        {
                            var progressRatio = (float)step / Plugin.Config.ZoomInSteps;

                            // Use a curve that provides consistent middle steps but accelerates at extremes
                            var curveValue = progressRatio < 0.5f
                                ? 0.5f + progressRatio // Linear growth in first half
                                : 0.5f + progressRatio + (progressRatio - 0.5f) * progressRatio; // Accelerated growth in second half

                            var increment = Math.Max(minStepSize, (int)(zoomInRange * curveValue / Plugin.Config.ZoomInSteps * 1.8f));

                            var newValue = newArray[i + 1] + increment;
                            newArray[i] = Math.Min((int)ppuMax, newValue);
                        }

                        // Zoom Out - gradually decreasing decrements (making it smaller)
                        // Zoom Out - with minimum step size to avoid slowdown in middle
                        var zoomOutRange = ppu - ppuMin;
                        for (int i = centralIndex + 1, step = 1; i < newArray.Length; i++, step++)
                        {
                            var progressRatio = (float)step / Plugin.Config.ZoomOutSteps;

                            // Similar curve for zoom out - consistent middle steps, more aggressive at extremes
                            var curveValue = progressRatio < 0.5f
                                ? 0.5f + progressRatio // Linear decrease in first half
                                : 0.5f + progressRatio + (progressRatio - 0.5f) * progressRatio; // Accelerated decrease in second half

                            var decrement = Math.Max(minStepSize, (int)(zoomOutRange * curveValue / Plugin.Config.ZoomOutSteps * 1.8f));

                            var newValue = newArray[i - 1] - decrement;
                            newArray[i] = Math.Max((int)ppuMin, newValue);
                        }

                        for (int i = 0; i < newArray.Length; i++)
                        {
                            Plugin.Logger.Log($"newArray zoomLevels level {newArray[i]}");
                        }

                        //// Zoom In
                        //int increment = 6;
                        //int[] increments = { 2, 4, 6 };
                        //int incrementIndex = 0;
                        //for (int i = centralIndex, value = ppu; i >= 0; i--)
                        //{
                        //    newArray[i] = value;
                        //    // value += increment;
                        //    value += increments[incrementIndex];
                        //    if ((i - centralIndex) % 2 == 0)
                        //    {
                        //        // increment = Math.Min(increment + 2, 10);
                        //        incrementIndex = (incrementIndex + 1) % increments.Length;
                        //    }
                        //}

                        //// Zoom Out
                        //
                        //int decrement = 6;
                        //int[] decrements = { 6, 4, 2 };
                        //int decrementIndex = 0;
                        //for (int i = centralIndex, value = ppu; i < newArray.Length; i++)
                        //{
                        //    newArray[i] = value;
                        //    value -= decrements[decrementIndex];
                        //    //value -= decrement;
                        //    if ((centralIndex - i) % 2 == 0)
                        //    {
                        //        //decrement = Math.Max(decrement - 2, 2);
                        //        decrementIndex = (decrementIndex + 1) % decrements.Length;
                        //    }
                        //}

                        //Array.Reverse(newArray); // I'm lazy

                        //_zoomLevels = newArray;
                        Plugin.Logger.Log($"Updating gamecamera zoom vars");

                        gameCamera._zoomLevels = newArray;
                        Plugin.Logger.Log($"Updated: gameCamera._zoomLevels");

                        gameCamera._defaultZoomIndex = centralIndex;
                        Plugin.Logger.Log($"Updated: gameCamera._defaultZoomIndex");

                        gameCamera._currentZoomIndex = centralIndex;
                        Plugin.Logger.Log($"Updated: gameCamera._currentZoomIndex");

                        if (_lastZoom > 0) // We need to update our camera last zoom
                        {
                            Plugin.Logger.Log($"_lastZoom > 0)");

                            if (_lastZoom < newArray.Length) // We within of array bounds
                            {
                                Plugin.Logger.Log($"_lastZoom < newArray.Length");
                                _lastZoom = centralIndex;
                                GameCamera._lastZoom = centralIndex;
                            }
                            else
                            {
                                Plugin.Logger.Log($"_lastZoom >= newArray.Length");
                                _lastZoom = -1;
                            }

                            if (GameCamera._lastZoom > 0) // We got some camera zoom saved
                            {
                                Plugin.Logger.Log($"GameCamera._lastZoom > 0");
                                _lastZoom = GameCamera._lastZoom;
                            }
                            else
                            {
                                Plugin.Logger.Log($"GameCamera._lastZoom > 0 ELSE");
                                // Set new lastzoom and currend zoom

                            }
                        }

                        Plugin.Logger.Log($"_lastZoom {_lastZoom}");

                        // Update our zoom PPU
                        Plugin.Logger.Log($"updatnig pixelPerfectCamera.assetsPPU {newArray[GameCamera._lastZoom]}");
                        gameCamera._pixelPerfectCamera.assetsPPU = newArray[GameCamera._lastZoom];

                        isInitialized = true;
                        Plugin.Logger.Log("Initialized");

                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.Log($"Error in initialization: {ex.Message}");
                    Plugin.Logger.Log($"{ex.StackTrace}");
                }
            }
        }

        private static void HandleCameraMovement()
        {
            if (isPanning)
            {
                return;
            }

            // Handle smooth zooming
            if (isZooming && dungeonGameMode != null)
            {
                var gameCamera = dungeonGameMode._camera;
                float elapsedTime = Time.time - zoomStartTime;
                float progress = Mathf.Clamp01(elapsedTime / zoomDuration);

                // Use smooth curve for zoom transition
                float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

                // Interpolate PPU value
                float currentPPU = Mathf.Lerp(oldPPU, newPPU, smoothProgress);
                gameCamera._pixelPerfectCamera.assetsPPU = (int)currentPPU;

                // Check if zoom animation is complete
                if (progress >= 1f)
                {
                    // Ensure final PPU is exactly the target value
                    gameCamera._pixelPerfectCamera.assetsPPU = (int)newPPU;
                    isZooming = false;
                    Plugin.Logger.Log($"Smooth zoom complete: {oldPPU} -> {newPPU}");
                }

                return; // Don't process camera movement while zooming
            }

            if (cameraNeedMoving && dungeonGameMode != null)
            {
                var gameCamera = dungeonGameMode._camera;
                Camera camera = gameCamera.Camera;

                // Store current camera position before moving
                Vector3 currentCameraPos = gameCamera.transform.position;
                storedCameraPosition = currentCameraPos;
                lastCameraPositionChangeTime = Time.time;

                // Predict mouse world position after zoom instead of calculating it after
                mouseWorldPosAfter = PredictMouseWorldPositionAfterZoom(gameCamera._currentZoomIndex);

                // Calculate the difference - this is how much the world point under cursor will shift
                Vector3 worldShift = mouseWorldPosAfter - mouseWorldPosBefore;

                // Move camera by the opposite of this shift to keep the same world point under cursor
                Vector3 targetCameraPos = currentCameraPos - worldShift;

                Plugin.Logger.Log($"Predicted mouse world pos after zoom: {mouseWorldPosAfter}");
                Plugin.Logger.Log($"Mouse world pos before zoom: {mouseWorldPosBefore}");
                Plugin.Logger.Log($"World shift: {worldShift}");
                Plugin.Logger.Log($"Target camera pos: {targetCameraPos}");

                // Move camera to compensate for the zoom shift
                gameCamera.MoveCameraToPosition(targetCameraPos, cameraMoveSpeed);

                // Start smooth zoom transition
                StartSmoothZoom();

                // Set camera mode if needed
                gameCamera.SetCameraMode(CameraMode.BorderMove);
                cameraNeedMoving = false;
                cooldownInProgress = true;

                // Start smooth zoom transition
                StartSmoothZoom();
            }

            if (cooldownInProgress)
            {
                // Check current camera position after movement command
                Vector3 currentPos = dungeonGameMode._camera.transform.position;

                // Check if camera position has changed since last frame
                if (Vector3.Distance(currentPos, storedCameraPosition) > cameraPositionTolerance)
                {
                    // Camera is still moving, update stored position and time
                    storedCameraPosition = currentPos;
                    lastCameraPositionChangeTime = Time.time;
                    Plugin.Logger.Log($"Camera still moving - Current pos: {currentPos}");
                }
                else
                {
                    Plugin.Logger.Log($"Camera not moving - Current pos: {currentPos}");
                }

                // Check if we can stop camera movement handling
                bool cooldownComplete = Time.time - lastZoomTime > zoomCooldown;
                bool cameraStoppedMoving = Time.time - lastCameraPositionChangeTime > cameraStoppedThreshold;
                bool zoomComplete = !isZooming; // Also wait for zoom to complete

                if (cooldownComplete && cameraStoppedMoving && zoomComplete)
                {
                    Plugin.Logger.Log($"Camera movement complete - Cooldown: {cooldownComplete}, Camera stopped: {cameraStoppedMoving}, Zoom complete: {zoomComplete}");
                    cooldownInProgress = false;
                }
            }
        }


        private static void StartSmoothZoom()
        {
            if (dungeonGameMode == null) return;

            var gameCamera = dungeonGameMode._camera;

            // Get new PPU from zoom levels array
            newZoomIndex = gameCamera._currentZoomIndex;
            newPPU = gameCamera._zoomLevels[newZoomIndex];

            // Only start smooth zoom if there's a difference
            if (Mathf.Abs(oldPPU - newPPU) > 0.1f)
            {
                isZooming = true;
                zoomStartTime = Time.time;

                Plugin.Logger.Log($"Starting smooth zoom: {oldPPU} -> {newPPU} (index: {newZoomIndex})");
            }
            else
            {
                // No significant change, just set the value directly
                gameCamera._pixelPerfectCamera.assetsPPU = (int)newPPU;

                Plugin.Logger.Log($"Starting smooth zoom: No significant change, setting the value directly {oldPPU} -> {newPPU}");
            }
        }

        private static void HandleCameraPanning()
        {
            if (dungeonGameMode == null) return;

            var gameCamera = dungeonGameMode._camera;
            if (gameCamera == null) return;

            // Check for pan input (Middle mouse button, Mouse button 4, or Mouse button 5)
            bool panInputPressed = Input.GetMouseButton(2) || // Middle mouse button
                                  Input.GetMouseButton(3) || // Mouse button 4 (back)
                                  Input.GetMouseButton(4);   // Mouse button 5 (forward)

            if (panInputPressed)
            {
                if (!isPanning)
                {
                    // Start panning
                    isPanning = true;
                    lastPanMousePosition = Input.mousePosition;
                    Plugin.Logger.Log("Started camera panning");
                }
                else
                {
                    // Continue panning
                    Vector3 currentMousePosition = Input.mousePosition;
                    Vector3 mouseDelta = currentMousePosition - lastPanMousePosition;

                    if (mouseDelta.magnitude > 0.1f) // Only pan if there's meaningful movement
                    {
                        // Convert mouse delta to world space movement
                        Camera camera = gameCamera.Camera;

                        // Calculate world space movement based on current zoom level
                        float worldUnitsPerPixel = camera.orthographicSize * 2f / Screen.height;
                        Vector3 worldDelta = new Vector3(-mouseDelta.x * worldUnitsPerPixel * panSensitivity,
                                                        -mouseDelta.y * worldUnitsPerPixel * panSensitivity,
                                                        0);

                        // Apply the movement to camera
                        Vector3 currentCameraPos = gameCamera.transform.position;
                        Vector3 targetCameraPos = currentCameraPos + worldDelta;

                        Plugin.Logger.Log($"Panning camera from {currentCameraPos} to {targetCameraPos} (delta: {worldDelta})");

                        // Move camera immediately for responsive panning
                        gameCamera.MoveCameraToPosition(targetCameraPos, 0f);

                        // Set camera mode for panning
                        gameCamera.SetCameraMode(CameraMode.BorderMove);
                    }

                    lastPanMousePosition = currentMousePosition;
                }
            }
            else
            {
                if (isPanning)
                {
                    // Stop panning
                    isPanning = false;
                    Plugin.Logger.Log("Stopped camera panning");
                }
            }
        }


        private static Vector3 PredictMouseWorldPositionAfterZoom(int newZoomIndex)
        {
            if (dungeonGameMode == null) return Vector3.zero;

            var gameCamera = dungeonGameMode._camera;
            Camera camera = gameCamera.Camera;

            // Get current mouse screen position
            Vector3 mouseScreenPos = Input.mousePosition;

            // Get current and new PPU values
            float currentPPU = gameCamera._pixelPerfectCamera.assetsPPU;
            float newPPU = gameCamera._zoomLevels[newZoomIndex];

            // Calculate the scale factor between old and new zoom
            float zoomScaleFactor = currentPPU / newPPU;

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
    }
}

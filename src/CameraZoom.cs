using HarmonyLib;
using MGSC;
using System;
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
        private static bool cameraZoomIn;
        private static CellPosition cellPosition;
        private static Vector3 mouseWorldPosBefore;
        private static Vector3 mouseWorldPosAfter;

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
            public static void Prefix()
            {
                if (dungeonGameMode != null)
                {
                    // Get mouse position in screen coordinates
                    Vector3 mouseScreenPos = Input.mousePosition;

                    // Convert mouse screen position to world coordinates at current zoom level
                    Camera camera = dungeonGameMode._camera.Camera;
                    mouseWorldPosBefore = camera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, camera.nearClipPlane));
                    Plugin.Logger.Log($"ZoomIn_Patch - Mouse world pos before zoom: {mouseWorldPosBefore}");
                    cameraNeedMoving = true;
                    cameraZoomIn = true;
                }
            }

            [HarmonyPostfix]
            public static void Postfix()
            {

            }
        }

        [HarmonyPatch(typeof(GameCamera), "ZoomOut")]
        public static class ZoomOut_Patch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                if (dungeonGameMode != null)
                {
                    // Get mouse position in screen coordinates
                    Vector3 mouseScreenPos = Input.mousePosition;

                    // Convert mouse screen position to world coordinates at current zoom level
                    Camera camera = dungeonGameMode._camera.Camera;
                    mouseWorldPosBefore = camera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, camera.nearClipPlane));
                    Plugin.Logger.Log($"ZoomOut_Patch - Mouse world pos before zoom: {mouseWorldPosBefore}");
                    cameraNeedMoving = true;
                    cameraZoomIn = false;
                }
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
                        var ppuMin = 10f;
                        var ppuMax = 150f;

                        // Calculate 64 as percentage of max range: 64 is ~42.7% of 150, or ~54% of the range (10-150)
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

                        for (int i = 0; i < gameCamera._zoomLevels.Length; i++)
                        {
                            Plugin.Logger.Log($"newArray zoomLevels level {gameCamera._zoomLevels[i]}");
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
            if (cameraNeedMoving && dungeonGameMode != null)
            {
                Plugin.Logger.Log("ZoomIn_Patch Postfix");
                cameraNeedMoving = false;

                if (cameraZoomIn)
                {
                    var gameCamera = dungeonGameMode._camera;
                    Camera camera = gameCamera.Camera;

                    // Get mouse position in world coordinates after zoom
                    Vector3 mouseScreenPos = Input.mousePosition;
                    mouseWorldPosAfter = camera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, camera.nearClipPlane));

                    Plugin.Logger.Log($"ZoomIn_Patch - Mouse world pos after zoom: {mouseWorldPosAfter}");

                    // Calculate the difference - this is how much the world point under cursor has shifted
                    Vector3 worldShift = mouseWorldPosAfter - mouseWorldPosBefore;
                    Plugin.Logger.Log($"ZoomIn_Patch - World shift: {worldShift}");

                    // Move camera by the opposite of this shift to keep the same world point under cursor
                    Vector3 currentCameraPos = gameCamera.transform.position;
                    Vector3 targetCameraPos = currentCameraPos - worldShift;

                    Plugin.Logger.Log($"ZoomIn_Patch - Moving camera from {currentCameraPos} to {targetCameraPos}");

                    // Move camera to compensate for the zoom shift
                    gameCamera.MoveCameraToPosition(targetCameraPos, 0.1f); // Small duration for smooth transition

                    // Set camera mode if needed
                    gameCamera.SetCameraMode(CameraMode.BorderMove);
                }
                else
                {

                    Plugin.Logger.Log("ZoomOut_Patch Postfix");

                    var gameCamera = dungeonGameMode._camera;
                    Camera camera = gameCamera.Camera;
                    
                    // Get mouse position in world coordinates after zoom
                    Vector3 mouseScreenPos = Input.mousePosition;
                    mouseWorldPosAfter = camera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, camera.nearClipPlane));
                    
                    Plugin.Logger.Log($"ZoomOut_Patch - Mouse world pos after zoom: {mouseWorldPosAfter}");
                    
                    // Calculate the difference - this is how much the world point under cursor has shifted
                    Vector3 worldShift = mouseWorldPosAfter - mouseWorldPosBefore;
                    Plugin.Logger.Log($"ZoomOut_Patch - World shift: {worldShift}");
                    
                    // Move camera by the opposite of this shift to keep the same world point under cursor
                    Vector3 currentCameraPos = gameCamera.transform.position;
                    Vector3 targetCameraPos = currentCameraPos - worldShift;
                    
                    Plugin.Logger.Log($"ZoomOut_Patch - Moving camera from {currentCameraPos} to {targetCameraPos}");
                    
                    // Move camera to compensate for the zoom shift
                    gameCamera.MoveCameraToPosition(targetCameraPos, 0.1f); // Small duration for smooth transition
                    
                    // Set camera mode if needed
                    gameCamera.SetCameraMode(CameraMode.BorderMove);
                }
            }
        }
    }
}

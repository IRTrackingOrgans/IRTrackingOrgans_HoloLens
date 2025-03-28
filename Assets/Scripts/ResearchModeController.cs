﻿using UnityEngine;
using System;
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine.XR.WSA;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Threading;


#if !UNITY_EDITOR && UNITY_WSA
using Windows.Perception.Spatial;
using Windows.Storage;
using System.Threading.Tasks;
#endif

#if ENABLE_WINMD_SUPPORT
using HL2DinoPlugin;
#endif

/** @file           ResearchModeController.cs
 *  @brief          Main controller/interaction point in Unity for the HL2-DINO .dll
 *  
 *  @details        This will grab sensor images and tool-pose data from the C++ side and use it to pass
 *                  along to any interested parties in Unity
 * 
 *  @note           The logic for setting up the grayscale sensor image structure and grabbing image data from 
 *                  the C++ DLL is adapted from petergu684's HoloLens2-ResearchMode-Unity on GitHub.
 *                  Check it out for completeness!
 *
 *  @author         Hisham Iqbal
 *  @copyright      &copy; 2023 Hisham Iqbal
 */

public class ResearchModeController : MonoBehaviour
{
#if ENABLE_WINMD_SUPPORT
    /// <summary>
    /// The C# interface to the HL2-DINO DLL interface, generated with WinRT. Functions exposed by this
    /// mirror the contents of the .idl file in the DLL project
    /// </summary>
    HL2ResearchModeController researchModeController;
#endif

    //! @name Unity variables for displaying sensor images 
    //!@{

    public GameObject depthPreviewPlane = null;
    private Material depthMediaMaterial = null;
    private Texture2D depthMediaTexture = null;
    private byte[] depthFrameData = null;

    public GameObject abImagePreviewPlane = null;
    private Material abImageMediaMaterial = null;
    private Texture2D abMediaTexture = null;
    private byte[] abFrameData = null;

    public TMPro.TextMeshProUGUI ConsoleDebugTextMesh;

    /// <summary>
    /// Use to internally track if sensor images are updated, but also pass this into \p HL2ResearchMode to 
    /// tell it if it should continue/stop processing sensor images for display purposes.
    /// </summary>
    bool SensorImagesDisplaying = true;

    //!@}

    /// <summary>
    /// Should be set from inspector. Class is responsible for using tool pose data obtained from 
    /// the DLL to set GameObject transforms in Unity.
    /// </summary>
    public UnityToolManager ToolManagerScript;

    public string JSONFilename = "toolConfig.json";
    string JSONStorageFolder = "";

    // Start is called before the first frame update
    void Start()
    {
        // (1) Caching/attaching Unity objects
        ImageTexturesSetup();

        // (2) Reading tool config data from file
        string toolConfigJSONString = ToolConfigJSONSetup();

        // (3) Launching HL2-DINO DLL if all is ok
        if (!string.IsNullOrEmpty(toolConfigJSONString))
        {
            // only set up with a valid string to avoid crashing
            ResearchModeSetup(toolConfigJSONString);
        }
    }

    /// <summary>
    /// Update class member vars to point to correct locations for where the tool config JSON file is
    /// </summary>
    private string ToolConfigJSONSetup()
    {
        // using this as a stand-in location, as StreamingAssets is compiled into the app.
        // Future TODO: explore reading directly from some headset location so you can change
        // tool config data at runtime, and without re-compiling
        JSONStorageFolder = Application.streamingAssetsPath;

        string toolConfigJSONString = ToolConfigUtilities.JSONUtils.GetJSONToolStringHL2(JSONStorageFolder + "/" + JSONFilename);
        if (!string.IsNullOrEmpty(toolConfigJSONString))
        {
            // if we get here, then the string should be properly JSON formatted
            // but it still will need to pass the same checks on the CPP side
#if UNITY_EDITOR
            print(toolConfigJSONString);
#endif
            return toolConfigJSONString;
        }
        else
        {
            ConsoleDebugTextMesh.text = $"{JSONFilename} not a valid JSON construct for this app";
            return string.Empty;
        }
    }

    /// <summary>
    /// Function which initialises DLL functions and instructs DLL which tools to track
    /// </summary>
    /// <param name="toolsetString">Ideally a JSON-formatted string of marker/tool triplet locations</param>
    private void ResearchModeSetup(string toolsetString)
    {
#if ENABLE_WINMD_SUPPORT
        researchModeController = new HL2ResearchModeController(toolsetString, true);        
        researchModeController.InitialiseDepthSensor();
        
        if (!SetupLocator()) // call will try to sync the world frame of Unity to the DLL
        {
            // if we're here, we failed to grab/pass locator information
            ConsoleDebugTextMesh.text = "App could not find a SpatialLocator";
            return;
        }

        // all good, so launch the DLL's depth sensor tool-detection loop
        researchModeController.StartDepthSensorLoop();
#endif
    }

    /// <summary>
    /// Attaching Unity GameObjects to their corresponding textures used to visualise images passed out of the DLL
    /// </summary>
    private void ImageTexturesSetup()
    {
        depthMediaMaterial = depthPreviewPlane.GetComponent<MeshRenderer>().material;
        depthMediaTexture = new Texture2D(512, 512, TextureFormat.Alpha8, false);
        depthMediaMaterial.mainTexture = depthMediaTexture;

        abImageMediaMaterial = abImagePreviewPlane.GetComponent<MeshRenderer>().material;
        abMediaTexture = new Texture2D(512, 512, TextureFormat.Alpha8, false);
        abImageMediaMaterial.mainTexture = abMediaTexture;

    }

    /// <summary>
    /// Function for receving the latest set of processed sensor images from the HL2, purely for visualisation purposes.
    /// It should be noted the numeric values of these images have no meaningful physical information like depth info.
    /// </summary>
    void GrabLatestSensorImages()
    {
#if ENABLE_WINMD_SUPPORT
        // monocular depth image
        if (researchModeController.Depth8BitImageUpdated())
        {
            byte[] frameTexture = researchModeController.Get8BitDepthImageBuf();
            if (frameTexture.Length > 0)
            { 
                if (depthFrameData == null) depthFrameData = frameTexture; // first frame
                else System.Buffer.BlockCopy(frameTexture, 0, depthFrameData, 0, depthFrameData.Length);
      
                depthMediaTexture.LoadRawTextureData(depthFrameData);
                depthMediaTexture.Apply();
            }
        }

        // a labelled image of infrared response. there should be crosses on detected tool marker-centres
        if (researchModeController.AB8BitImageUpdated())
        {
            byte[] frameTexture = researchModeController.Get8BitABImageBuf();
            if (frameTexture.Length > 0)
            {
                if (abFrameData == null) abFrameData = frameTexture;
                else System.Buffer.BlockCopy(frameTexture, 0, abFrameData, 0, abFrameData.Length);

                abMediaTexture.LoadRawTextureData(abFrameData);
                abMediaTexture.Apply();
            }
        }
#endif
    }

    /// <summary>
    /// Function for receiving tool pose matrices from the IR tracking class running on board the HL2
    /// </summary>
    void GrabLatestToolDictionary()
    {
#if ENABLE_WINMD_SUPPORT
        try
        {
            if (researchModeController.ToolDictionaryUpdated()) // true each time a new set of tool poses are updated on the C++ side
            {
                // grabs an encoded double arrays
                double[] toolsTransform = researchModeController.GetTrackedToolsPoseMatrices();
                // pass this information onto our tool manager
                if (toolsTransform != null) ToolManagerScript.EnqueueTrackingData(toolsTransform); 
            }
        }
        catch (Exception ex)
        {
            ConsoleDebugTextMesh.text = ex.StackTrace;
        }
#endif
    }

    void LateUpdate()
    {
        // main loop of this class
#if ENABLE_WINMD_SUPPORT
        GrabLatestSensorImages();
        GrabLatestToolDictionary();
#endif
    }

#if WINDOWS_UWP
    /// <summary>
    ///  Function tries to grab coordinate frame details from Unity's side, to pass into the HL2-DINO DLL.
    ///  This will allow the C++ DLL to try and track tools in the same coordinate frame being used in Unity.
    /// </summary>
    /// <returns></returns>
    bool SetupLocator()
    {
        // original source, instructive:
        //https://github.com/microsoft/MixedReality-SpectatorView/blob/master/src/HolographicCamera.Unity/Assets/HolographicCamera/Scripts/CameraPoseProvider.cs#L147-L150

        SpatialLocatability headset_init = SpatialLocatability.Unavailable;
        int i;
        for (i = 0; i < 20; i++) // 20 attempts before cutting out? (this isn't threaded, so it could be fatal...)
        {
            SpatialLocator locator = SpatialLocator.GetDefault();
            headset_init = locator.Locatability;
            if (headset_init == SpatialLocatability.PositionalTrackingActive) { break; }
        }

        if (headset_init != SpatialLocatability.PositionalTrackingActive) { return false; }

        SpatialCoordinateSystem unityCoordinateSystem =
            Marshal.GetObjectForIUnknown(WorldManager.GetNativeISpatialCoordinateSystemPtr()) as SpatialCoordinateSystem;

        if (unityCoordinateSystem == null) return false;

        if (researchModeController != null) { researchModeController.SetReferenceCoordinateSystem(unityCoordinateSystem); return true; }

        return false;
    }

    /// <summary>
    /// Helper function which will save the profiler string to the app's local folder
    /// Has to be retrieved from the Device Portal
    /// </summary>
    /// <param name="content"></param>
    /// <param name="filename">Filepath (with proper extension)</param>
    private async Task SaveProfilerString(string content, string filename)
    {
        try
        {
            string localFolder = ApplicationData.Current.LocalFolder.Path;
            string profiler_path = localFolder + "/" + filename;

            using (StreamWriter file = new StreamWriter(profiler_path))
            {
                await file.WriteAsync(content);
            }
        }
        catch (Exception ex)
        {
            ConsoleDebugTextMesh.text = $"Error: {ex.Message}";
        }
    }

#endif

    private void OnApplicationFocus(bool focus)
    {
        // if app is shutdown, try to stop the sensor loop running
        if (!focus) StopDepthSensor();
    }

    public void StopDepthSensor()
    {
#if ENABLE_WINMD_SUPPORT
        researchModeController.StopSensorLoop();
#endif
    }

    /// <summary>
    /// Public toggle function to tell the DLL side if we should be stashing sensor images for display
    /// or not
    /// </summary>
    public void SwitchSensorDisplayOnOff()
    {
        SensorImagesDisplaying = !SensorImagesDisplaying;
#if ENABLE_WINMD_SUPPORT
        researchModeController.ToggleDisplaySensorImages(SensorImagesDisplaying);
#endif
    }

    /// <summary>
    /// Profiler data as recorded by the Shiny library on the C++ DLL.
    /// 
    /// Proper thread safety has not been robustly tested, would suggest avoiding
    /// numerous calls from multiple sources, especially as you're dumping to a file.
    /// </summary>

#if ENABLE_WINMD_SUPPORT
    async
#endif
    public void FetchProfilerString()
    {
#if ENABLE_WINMD_SUPPORT
        string profileString = researchModeController.GetProfilerString();
        ConsoleDebugTextMesh.text = profileString;
        await SaveProfilerString(profileString, $"DINO-AR_Profile_Unity{Application.unityVersion}.txt");
#endif
    }

    private List<(float, Dictionary<int, ToolTrackingUtils.TrackedTool>)> recordedToolDictionaries = new List<(float, Dictionary<int, ToolTrackingUtils.TrackedTool>)>();
#if ENABLE_WINMD_SUPPORT
    async
#endif

    public void FetchTransform()
    {
#if ENABLE_WINMD_SUPPORT
    if (ToolManagerScript == null) return;

    // Clear the recordedToolDictionaries list before starting a new recording
    recordedToolDictionaries.Clear();

    Thread.Sleep(5000);

    // Start a coroutine to record transform data for 5 seconds
    StartCoroutine(RecordTransformDataForDuration(30f));
#endif
    }

    private IEnumerator RecordTransformDataForDuration(float duration)
    {
#if ENABLE_WINMD_SUPPORT
        float elapsedTime = 0f;



        while (elapsedTime < duration)
        {
            // Get the ToolDictionary
            IReadOnlyDictionary<int, ToolTrackingUtils.TrackedTool> transformToPrint = ToolManagerScript.GetToolDictionary();

            // Create a new dictionary to store the copy of transformToPrint
            Dictionary<int, ToolTrackingUtils.TrackedTool> transformCopy = new Dictionary<int, ToolTrackingUtils.TrackedTool>();

            // Deep copy each key-value pair from transformToPrint
            foreach (var pair in transformToPrint)
            {
                // Create a new TrackedTool instance with only Tool_HoloFrame_LH copied
                var copiedTool = new ToolTrackingUtils.TrackedTool();
                copiedTool.Tool_HoloFrame_LH = pair.Value.Tool_HoloFrame_LH;

                // Add the copied instance to the transformCopy dictionary
                transformCopy.Add(pair.Key, copiedTool);
            }

            // Copy
            recordedToolDictionaries.Add((elapsedTime, transformCopy));

            string debugInfo = $"Elapsed Time: {elapsedTime.ToString("F2")} / {duration}\n";
            ConsoleDebugTextMesh.text = debugInfo;

            // Increment the elapsed time
            elapsedTime += Time.deltaTime;

            // Wait for the next frame
            yield return null;
        }

        // Once recording is complete, save the recorded transform data
        SaveRecordedToolDictionary();
        
    }

    private void SaveRecordedToolDictionary()
    {
        // Combine all recorded ToolDictionaries into a single string
        string allToolDictionaries = "";

        foreach (var toolTuple in recordedToolDictionaries)
        {
            foreach (var pair in toolTuple.Item2)
            {
            allToolDictionaries += $"Elapsed Time: {toolTuple.Item1.ToString("F2")}\n";
            allToolDictionaries += $"Key: {pair.Key}\n";
            allToolDictionaries += $"{pair.Value.Tool_HoloFrame_LH.ToString("F3")}\n";
            }
        }

        // Save all recorded ToolDictionaries to a text file using SaveProfilerString
        SaveProfilerString(allToolDictionaries, $"DINO-AR_AllToolDictionary_Unity_v2{Application.unityVersion}.txt");

        // Clear the recorded ToolDictionaries list for the next recording
        recordedToolDictionaries.Clear();
#else
        yield break;
#endif
    }
}

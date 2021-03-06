/*
 * Copyright 2014 Google Inc. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System.Collections;
using UnityEngine;
using Tango;
using System;

/// <summary>
/// This is a basic movement controller based on
/// pose estimation returned from the Tango Service.
/// </summary>
public class PoseController : PoseListener
{
    public enum TrackingTypes
    {
        NONE,
        MOTION,
        ADF,
        RELOCALIZED
    }
    
    private bool m_alreadyInitialized = false;
    private TangoApplication m_tangoApplication;

    // Tango pose data for debug logging and transform update.
    [HideInInspector]
    public string m_tangoServiceVersionName = string.Empty;
    [HideInInspector]
    public float m_frameDeltaTime;
    [HideInInspector]
    public int m_frameCount;
    [HideInInspector]
    public TangoEnums.TangoPoseStatusType m_status;
    private float m_prevFrameTimestamp;

    // Tango pose data.
    private Quaternion m_tangoRotation;
    private Vector3 m_tangoPosition;
    private bool m_isDirty = false;

    // We use couple of matrix transformation to convert the pose from Tango coordinate
    // frame to Unity coordinate frame.
    // The full equation is:
    //     Matrix4x4 uwTuc = m_uwTss * ssTd * m_dTuc;
    //
    // uwTuc: Unity camera with respect to Unity world, this is the desired matrix.
    // m_uwTss: Constant matrix converting start of service frame to Unity world frame.
    // ssTd: Device frame with repect to start of service frame, this matrix denotes the 
    //       pose transform we get from pose callback.
    // m_dTuc: Constant matrix converting Unity world frame frame to device frame.
    //
    // Please see the coordinate system section online for more information:
    //     https://developers.google.com/project-tango/overview/coordinate-systems
    private Matrix4x4 m_uwTss;
    private Matrix4x4 m_dTuc;

    // Flag for initilizing Tango.
    private bool m_shouldInitTango = false;
    
    /// <summary>
    /// Initialize the controller.
    /// </summary>
    private void Awake()
    {
        // Constant matrix converting start of service frame to Unity world frame.
        m_uwTss = new Matrix4x4();
        m_uwTss.SetColumn (0, new Vector4 (1.0f, 0.0f, 0.0f, 0.0f));
        m_uwTss.SetColumn (1, new Vector4 (0.0f, 0.0f, 1.0f, 0.0f));
        m_uwTss.SetColumn (2, new Vector4 (0.0f, 1.0f, 0.0f, 0.0f));
        m_uwTss.SetColumn (3, new Vector4 (0.0f, 0.0f, 0.0f, 1.0f));

        // Constant matrix converting Unity world frame frame to device frame.
        m_dTuc = new Matrix4x4();
        m_dTuc.SetColumn (0, new Vector4 (1.0f, 0.0f, 0.0f, 0.0f));
        m_dTuc.SetColumn (1, new Vector4 (0.0f, 1.0f, 0.0f, 0.0f));
        m_dTuc.SetColumn (2, new Vector4 (0.0f, 0.0f, -1.0f, 0.0f));
        m_dTuc.SetColumn (3, new Vector4 (0.0f, 0.0f, 0.0f, 1.0f));

        m_isDirty = false;
        m_frameDeltaTime = -1.0f;
        m_prevFrameTimestamp = -1.0f;
        m_frameCount = -1;
        m_status = TangoEnums.TangoPoseStatusType.NA;
        m_tangoRotation = Quaternion.identity;
        m_tangoPosition = Vector3.zero;
    }

    /// <summary>
    /// Start this instance.
    /// </summary>
    private void Start()
    {
        m_tangoApplication = FindObjectOfType<TangoApplication>();
        
        if(m_tangoApplication != null)
        {
            if(AndroidHelper.IsTangoCorePresent())
            {
                // Request Tango permissions
                m_tangoApplication.RegisterPermissionsCallback(_OnTangoApplicationPermissionsEvent);
                m_tangoApplication.RequestNecessaryPermissionsAndConnect();
                m_tangoServiceVersionName = TangoApplication.GetTangoServiceVersion();
            }
            else
            {
                // If no Tango Core is present let's tell the user to install it!
                StartCoroutine(_InformUserNoTangoCore());
            }
        }
        else
        {
            Debug.Log("No Tango Manager found in scene.");
        }
    }
    
    /// <summary>
    /// Informs the user that they should install Tango Core via Android toast.
    /// </summary>
    private IEnumerator _InformUserNoTangoCore()
    {
        AndroidHelper.ShowAndroidToastMessage("Please install Tango Core", false);
        yield return new WaitForSeconds(2.0f);
        Application.Quit();
    }
    
    /// <summary>
    /// Apply any needed changes to the pose.
    /// </summary>
    private void Update()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        if (m_shouldInitTango)
        {
            m_tangoApplication.InitApplication();
            m_tangoApplication.InitProviders(string.Empty);
            m_tangoApplication.ConnectToService();
            m_shouldInitTango = false;
        }
        if (m_isDirty)
        {
            // Construct the start of service with respect to device matrix from the pose.
            Matrix4x4 ssTd = Matrix4x4.TRS(m_tangoPosition, m_tangoRotation, Vector3.one);

            // Converting from Tango coordinate frame to Unity coodinate frame.
            Matrix4x4 uwTuc = m_uwTss * ssTd * m_dTuc;
            
            // Extract new local position
            transform.position = uwTuc.GetColumn(3);
            
            // Extract new local rotation
            transform.rotation = Quaternion.LookRotation(uwTuc.GetColumn(2), uwTuc.GetColumn(1));
        }
        
        if(Input.GetKeyDown(KeyCode.Escape))
        {
            if(m_tangoApplication != null)
            {
                m_tangoApplication.Shutdown();
            }
            
            // This is a temporary fix for a lifecycle issue where calling
            // Application.Quit() here, and restarting the application immediately,
            // results in a hard crash.
            AndroidHelper.AndroidQuit();
        }
        
        #else
        Vector3 tempPosition = transform.position;
        Quaternion tempRotation = transform.rotation;
        PoseProvider.GetMouseEmulation(ref tempPosition, ref tempRotation);
        transform.rotation = tempRotation;
        transform.position = tempPosition;
        #endif
    }

    /// <summary>
    /// Unity callback when application is paused.
    /// </summary>
    private void OnApplicationPause(bool pauseStatus)
    {
        m_isDirty = false;
        m_frameDeltaTime = -1.0f;
        m_prevFrameTimestamp = -1.0f;
        m_frameCount = -1;
        m_status = TangoEnums.TangoPoseStatusType.NA;
        m_tangoRotation = Quaternion.identity;
        m_tangoPosition = Vector3.zero;
    }
    
    /// <summary>
    /// Handle the callback sent by the Tango Service
    /// when a new pose is sampled.
    /// DO NOT USE THE UNITY API FROM INSIDE THIS FUNCTION!
    /// </summary>
    /// <param name="callbackContext">Callback context.</param>
    /// <param name="pose">Pose.</param>
    protected override void _OnPoseAvailable(IntPtr callbackContext, TangoPoseData pose)
    {
        // Get out of here if the pose is null
        if (pose == null)
        {
            Debug.Log("TangoPoseDate is null.");
            return;
        }
        
        // The callback pose is for device with respect to start of service pose.
        if (pose.framePair.baseFrame == TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_START_OF_SERVICE &&
            pose.framePair.targetFrame == TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE)
        {
            if(pose.status_code == TangoEnums.TangoPoseStatusType.TANGO_POSE_VALID)
            {
                // Cache the position and rotation to be set in the update function.
                // This needs to be done because this callback does not
                // happen in the main game thread.
                m_tangoPosition = new Vector3((float)pose.translation [0],
                                              (float)pose.translation [1],
                                              (float)pose.translation [2]);

                m_tangoRotation = new Quaternion((float)pose.orientation [0],
                                                 (float)pose.orientation [1],
                                                 (float)pose.orientation [2],
                                                 (float)pose.orientation [3]);
            }
            else // if the current pose is not valid we set the pose to identity
            {
                m_tangoPosition = Vector3.zero;
                m_tangoRotation = Quaternion.identity;
            }
        }
        
        // Reset the current status frame count if the status code changed.
        if (pose.status_code != m_status)
        {
            m_frameCount = 0;
        }
        
        // Update the stats for the pose for the debug text
        m_status = pose.status_code;
        m_frameCount++;
        
        // Compute delta frame timestamp.
        m_frameDeltaTime = (float)pose.timestamp - m_prevFrameTimestamp;
        m_prevFrameTimestamp = (float)pose.timestamp;
        
        // Switch m_isDirty to true, so that the new pose get rendered in update.
        m_isDirty = (pose.status_code == TangoEnums.TangoPoseStatusType.TANGO_POSE_VALID);
    }
    
    private void _OnTangoApplicationPermissionsEvent(bool permissionsGranted)
    {
        if(permissionsGranted)
        {
            m_shouldInitTango = true;
        }
        else
        {
            AndroidHelper.ShowAndroidToastMessage("Motion Tracking Permissions Needed", true);
        }
    }
}

﻿/*
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
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using UnityEngine;
using Tango;

/// <summary>
/// Point cloud visualize using depth frame API.
/// </summary>
public class Pointcloud : DepthListener
{
    [HideInInspector]
    public float m_overallZ = 0.0f;
    [HideInInspector]
    public int m_pointsCount = 0;
    [HideInInspector]
    public float m_depthDeltaTime = 0.0f;

    private TangoApplication m_tangoApplication;

    // Matrices for transforming pointcloud to world coordinates.
    // This equation will take account of the camera sensors extrinsic.
    // Full equation is:
    //   Matrix4x4 uwTuc = m_uwTss * m_ssTd * Matrix4x4.Inverse(m_imuTd) * m_imuTc * m_cTuc;
    private Matrix4x4 m_uwTss = new Matrix4x4 ();
    private Matrix4x4 m_ssTd = new Matrix4x4 ();
    private Matrix4x4 m_imuTd = new Matrix4x4 ();
    private Matrix4x4 m_imuTc = new Matrix4x4();
    private Matrix4x4 m_cTuc = new Matrix4x4 ();

    // Some const value.
    private const int VERT_COUNT = 61440;
    
    // m_vertices will be assigned to this mesh.
    private Mesh m_mesh;
    
    // Mesh data.
    private Vector3[] m_vertices;
    private int[] m_triangles;
    private bool m_isDirty;
    
    // Logging data.
    private double m_previousDepthDeltaTime = 0.0;
    private bool m_isExtrinsicQuerable = false;

    /// <summary>
    /// Use this for initialization.
    /// </summary>
    public void Start() 
    {
        m_tangoApplication = FindObjectOfType<TangoApplication>();
        m_isDirty = false;

        m_uwTss.SetColumn (0, new Vector4 (1.0f, 0.0f, 0.0f, 0.0f));
        m_uwTss.SetColumn (1, new Vector4 (0.0f, 0.0f, 1.0f, 0.0f));
        m_uwTss.SetColumn (2, new Vector4 (0.0f, 1.0f, 0.0f, 0.0f));
        m_uwTss.SetColumn (3, new Vector4 (0.0f, 0.0f, 0.0f, 1.0f));

        m_cTuc.SetColumn (0, new Vector4 (1.0f, 0.0f, 0.0f, 0.0f));
        m_cTuc.SetColumn (1, new Vector4 (0.0f, -1.0f, 0.0f, 0.0f));
        m_cTuc.SetColumn (2, new Vector4 (0.0f, 0.0f, 1.0f, 0.0f));
        m_cTuc.SetColumn (3, new Vector4 (0.0f, 0.0f, 0.0f, 1.0f));

        m_vertices = new Vector3[VERT_COUNT];
        m_triangles = new int[VERT_COUNT];
        // Assign triangles, note: this is just for visualizing point in the mesh data.
        for (int i = 0; i < VERT_COUNT; i++)
        {
            m_triangles[i] = i;
        }

        m_mesh = GetComponent<MeshFilter>().mesh;

        m_mesh.Clear();
        m_mesh.vertices = m_vertices;
        m_mesh.triangles = m_triangles;
        m_mesh.RecalculateBounds();
        m_mesh.RecalculateNormals();
    }

    /// <summary>
    /// Update is called once per frame.
    /// </summary>
    private void Update() 
    {
        if (m_isDirty)
        {
            double timestamp = 0.0;
            TangoCoordinateFramePair pair;
            TangoPoseData poseData = new TangoPoseData();

            // Query the extrinsics between IMU and device frame.
            pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_IMU;
            pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
            PoseProvider.GetPoseAtTime(poseData, timestamp, pair);
            Vector3 position = new Vector3((float)poseData.translation[0],
                                           (float)poseData.translation[1],
                                           (float)poseData.translation[2]);
            Quaternion quat = new Quaternion((float)poseData.orientation[0],
                                             (float)poseData.orientation[1],
                                             (float)poseData.orientation[2],
                                             (float)poseData.orientation[3]);
            m_imuTd = Matrix4x4.TRS(position, quat, new Vector3 (1.0f, 1.0f, 1.0f));
            
            // Query the extrinsics between IMU and color camera frame.
            pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_IMU;
            pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_CAMERA_COLOR;
            PoseProvider.GetPoseAtTime(poseData, timestamp, pair);
            position = new Vector3((float)poseData.translation[0],
                                   (float)poseData.translation[1],
                                   (float)poseData.translation[2]);
            quat = new Quaternion((float)poseData.orientation[0],
                                  (float)poseData.orientation[1],
                                  (float)poseData.orientation[2],
                                  (float)poseData.orientation[3]);
            m_imuTc = Matrix4x4.TRS(position, quat, new Vector3 (1.0f, 1.0f, 1.0f));
            
            // Query pose to transform point cloud to world coordinates, here we are using the timestamp
            // that we get from depth.
            pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_START_OF_SERVICE;
            pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
            PoseProvider.GetPoseAtTime(poseData, m_previousDepthDeltaTime, pair);
            position = new Vector3((float)poseData.translation[0],
                                   (float)poseData.translation[1],
                                   (float)poseData.translation[2]);
            quat = new Quaternion((float)poseData.orientation[0],
                                  (float)poseData.orientation[1],
                                  (float)poseData.orientation[2],
                                  (float)poseData.orientation[3]);
            m_ssTd = Matrix4x4.TRS(position, quat, Vector3.one);

            // The transformation matrix that represents the pointcloud's pose.
            Matrix4x4 uwTuc = m_uwTss * m_ssTd * Matrix4x4.Inverse(m_imuTd) * m_imuTc * m_cTuc;

            transform.position = uwTuc.GetColumn(3);
            transform.rotation = Quaternion.LookRotation(uwTuc.GetColumn(2), uwTuc.GetColumn(1));

            m_mesh.Clear();
            m_mesh.vertices = m_vertices;
            m_mesh.triangles = m_triangles;
            m_mesh.SetIndices(m_triangles, MeshTopology.Points, 0);

            m_isDirty = false;
        }
    }

    /// <summary>
    /// Callback that gets called when depth is available
    /// from the Tango Service.
    /// DO NOT USE THE UNITY API FROM INSIDE THIS FUNCTION!
    /// </summary>
    /// <param name="callbackContext">Callback context.</param>
    /// <param name="xyzij">Xyzij.</param>
    protected override void _OnDepthAvailable(IntPtr callbackContext, TangoXYZij xyzij)
    {
        // Calculate the time since the last successful depth data
        // collection.
        if (m_previousDepthDeltaTime == 0.0)
        {
            m_previousDepthDeltaTime = xyzij.timestamp;
        }
        else
        {
            m_depthDeltaTime = (float)((xyzij.timestamp - m_previousDepthDeltaTime) * 1000.0);
            m_previousDepthDeltaTime = xyzij.timestamp;
        }

        // Fill in the data to draw the point cloud.
        if (xyzij != null && m_vertices != null)
        {
            int numberOfActiveVertices = xyzij.xyz_count;
            m_pointsCount = numberOfActiveVertices;
            float validPointCount = 0;
            if(numberOfActiveVertices > 0)
            {
                float[] allPositions = new float[numberOfActiveVertices * 3];
                Marshal.Copy(xyzij.xyz[0], allPositions, 0, allPositions.Length);
                
                for(int i = 0; i < m_vertices.Length; ++i)
                {
                    if( i < xyzij.xyz_count )
                    {
                        // Note that we are doing a simple axis switch to convert the point from
                        // depth camera coordinate frame to Unity coordinate frame.
                        m_vertices[i].x = allPositions[i * 3];
                        m_vertices[i].y = -allPositions[(i * 3) + 1];
                        m_vertices[i].z = allPositions[(i * 3) + 2];

                        m_overallZ += m_vertices[i].z;
                        ++validPointCount;
                    }
                    else
                    {
                        m_vertices[i].x = m_vertices[i].y = m_vertices[i].z = 0.0f;
                    }
                }
                m_isDirty = true;
            }
            // Don't divide by zero!
            if (validPointCount != 0)
            {
                m_overallZ = m_overallZ / validPointCount;
            } 
            else
            {
                m_overallZ = 0;
            }
        }
    }
}

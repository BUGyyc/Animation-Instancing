﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AnimationInstancing
{
    class UtilityHelper
    {
        /// <summary>
        /// ! 得到最后的转换矩阵数据
        /// </summary>
        /// <param name="bonePose"></param>
        /// <param name="bindPose"></param>
        /// <param name="rootMatrix1stFrame"></param>
        /// <param name="haveRootMotion"></param>
        /// <returns></returns>
        public static Matrix4x4[] CalculateSkinMatrix(Transform[] bonePose,
            Matrix4x4[] bindPose,
            Matrix4x4 rootMatrix1stFrame,
            bool haveRootMotion)
        {
            if (bonePose.Length == 0)
                return null;
            
            //! 找到 Root 节点
            Transform root = bonePose[0];
            while (root.parent != null)
            {
                root = root.parent;
            }
            //！取下 Root 节点的变换矩阵
            Matrix4x4 rootMat = root.worldToLocalMatrix;

            //！计算最终的矩阵
            Matrix4x4[] matrix = new Matrix4x4[bonePose.Length];
            for (int i = 0; i != bonePose.Length; ++i)
            {
                matrix[i] = rootMat * bonePose[i].localToWorldMatrix * bindPose[i];
            }
            return matrix;
        }


        /// <summary>
        /// ！把数据拷贝下来
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="src"></param>
        public static void CopyMatrixData(GenerateOjbectInfo dst, GenerateOjbectInfo src)
        {
            dst.animationTime = src.animationTime;
            dst.boneListIndex = src.boneListIndex;
            dst.frameIndex = src.frameIndex;
            dst.nameCode = src.nameCode;
            dst.stateName = src.stateName;
            dst.worldMatrix = src.worldMatrix;
            dst.boneMatrix = src.boneMatrix;
        }

        /// <summary>
        ///! Matrix To Color
        /// </summary>
        /// <param name="boneMatrix"></param>
        /// <returns></returns>
        public static Color[] Convert2Color(Matrix4x4[] boneMatrix)
        {
            Color[] color = new Color[boneMatrix.Length * 4];
            int index = 0;
            
            //! 用四个颜色存储四行数据
            foreach (var obj in boneMatrix)
            {
                color[index++] = obj.GetRow(0);
                color[index++] = obj.GetRow(1);
                color[index++] = obj.GetRow(2);
                color[index++] = obj.GetRow(3);
            }
            return color;
        }
    }
}

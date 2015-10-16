﻿/*-----------------------------------------------------------------------
//文件名：T28181VideoMonitor.cs
//版权：Copyright 2011-2012 Huawei Tech. Co. Ltd. All Rights Reserved. 
//作者：w00206574
//日期：2014-3-06
//描述：用于对接以T28181协议开放的平台，解析返回数据
//---------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CgwMonitorManage.Common;
using CgwMonitorManage.SmcError;
using CgwMonitorManage.NLogEx;
using System.Threading;
using System.Runtime.InteropServices;

namespace CgwMonitorManage.T28181
{
    public class T28181VideoMonitor : IVideoMonitor
    {

        /// <summary>
        /// 获取设备是否结束
        /// </summary>
        bool isGetDevicesFinish = true;

        /// <summary>
        /// 刷新设备列表等待时间
        /// </summary>
        uint refreshDeviceListOverTime = 0;

        /// <summary>
        /// 设备缓存列表未被使用等待时间
        /// </summary>
        uint deviceListUnusedOverTime = 0;

        /// <summary>
        /// T28181监控平台的sip服务器IP地址
        /// </summary>
        private string domain = string.Empty;

        /// <summary>
        /// T28181监控平台的sip服务器端口
        /// </summary>
        private string sipPort = string.Empty;

        /// <summary>
        /// 要查询的T28181监控平台的设备根编码（会查询该编码下所有设备）
        /// </summary>
        private string deviceID = string.Empty;

        /// <summary>
        /// 平台类型
        /// </summary>
        private string platformType = string.Empty;

        /// <summary>
        /// 网关对接T28181监控平台时，网关视为监控平台外域，该编码是标识网关的域编码，即是监控平台配置界面添加网关时的外域编码。
        /// </summary>
        private string localSignalGateway = string.Empty;

        /// <summary>
        /// T28181监控平台的域编码。
        /// </summary>
        private string serverSignalGateway = string.Empty;

        /// <summary>
        /// T28181监控平台登陆网关的sip鉴权用户名
        /// </summary>
        private string serverSipAccount = string.Empty;

        /// <summary>
        /// T28181监控平台登陆网关的sip鉴权密码
        /// </summary>
        //private string serverSipPasswd = string.Empty;

        /// <summary>
        /// 本地SIP端口
        /// </summary>
        private string localPort = string.Empty;

        /// <summary>
        /// 查询设备目录超时（秒）
        /// </summary>
        private int iQueryDeviceTimeOut = 0;

        /// <summary>
        /// 登陆T28181监控平台的sip用户名
        /// </summary>
        private string username = string.Empty;

        /// <summary>
        /// 登陆T28181监控平台的sip加密密码
        /// </summary>
        //private string password = string.Empty;
        private byte[] pwdByte = null;

        /// <summary>
        /// 日志
        /// </summary>
        private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 向上码流回调函数
        /// </summary>
        private DataCallBack dataCallBack;

        /// <summary>
        /// 标示查询设备列表结束，完成查询后向SMC传送查询结果
        /// </summary>
        private bool getDeviceEndFlg = true;

        /// <summary>
        /// 码流回调者
        /// </summary>
        private string sender;

        /// <summary>
        /// 监控平台ID
        /// </summary>
        private string monitorId;

        /// <summary>
        /// 保存摄像头跟预览通道的关系，key为摄像头编号，value为预览通道，一对一的关系
        /// </summary>
        private Dictionary<string, UInt32> cameraVideoChannelDic = new Dictionary<string, UInt32>();

        /// <summary>
        /// 保存预览通道跟码流发送类对象的关系，key为预览通道，value为码流发送类对象，一对一的关系
        /// </summary>
        private Dictionary<UInt32, MediaDataSender> videoChannelDataSenderDic = new Dictionary<UInt32, MediaDataSender>();

        /// <summary>
        /// 预览句柄操作锁，用于cameraVideoChannelDic、videoChannelDataSenderDic的操作
        /// </summary>
        private ReaderWriterLockSlim handelOperateLock = new ReaderWriterLockSlim();

        /// <summary>
        /// 保存摄像头和mic状态，key为camera编号，value为mic是否开启标记
        /// </summary>
        private Dictionary<string, bool> cameraMicStatusDic = new Dictionary<string, bool>();

        /// <summary>
        /// mic状态dictionary操作锁
        /// </summary>
        private ReaderWriterLockSlim micOperateLock = new ReaderWriterLockSlim();

        /// <summary>
        /// 摄像头操作锁
        /// </summary>
        private ReaderWriterLockSlim cameraOperateLock = new ReaderWriterLockSlim();

        /// <summary>
        /// 摄像头列表
        /// </summary>
        private List<Camera> cameraList = new List<Camera>();

        /// <summary>
        /// 自定义组列表
        /// </summary>
        private List<CameraGroup> groupList = new List<CameraGroup>();

        //定时器器，用于定时更新摄像头
        private System.Timers.Timer updateCameraTimer = new System.Timers.Timer();

        /// <summary>
        /// 分组管理列表
        /// </summary>
        private List<NodeRelation> nodeRelationList = new List<NodeRelation>();

        /// <summary>
        /// sip协议栈
        /// </summary>
        private SipStackAdapter sipStack = new SipStackAdapter();

        /// <summary>
        /// 向下码流回调，为了防止回调函数被销毁导致码流回调失败，定义为成员变量 
        /// </summary>
        private NET_DATA_CALLBACK realPlayCallback = new NET_DATA_CALLBACK(RealPlayCallBackRawFun);

        /// <summary>
        /// NetSource收包异常
        /// </summary>
        private NET_EXCEPTION_CALLBACK netExceptionCallBack = new NET_EXCEPTION_CALLBACK(NetExceptionCallBackFun);

        /// <summary>
        /// 解析rtp包回调函数
        /// </summary>
        static private FrameDataCallBack frameDataCallBack;

        /// <summary>
        /// rtp数据包解析类
        /// </summary>
        static private RtpAdapter rtpAdapter = new RtpAdapter();

        //定时器器，用于定时刷新日志
        private System.Timers.Timer flushLogTimer = new System.Timers.Timer();

        /// <summary>
        /// 定时器器，用于获取设备列表
        /// </summary>
        private System.Timers.Timer monitorManageServiceGetCameraList = new System.Timers.Timer();
        /// <summary>
        /// 构造函数，设置查询摄像机线程属性
        /// </summary>
        public T28181VideoMonitor()
        {
            //初始话定时器
            this.updateCameraTimer.AutoReset = true;
            this.updateCameraTimer.Elapsed += new System.Timers.ElapsedEventHandler(GetAllCamerasTimer);
            //摄像头变化不会太快，间隔时间暂定为1小时
            this.updateCameraTimer.Interval = CgwConst.REFRESH_CAMERA_LIST_WAIT_TIME;

            //定时刷新日志
            //this.flushLogTimer.AutoReset = true;
            //this.flushLogTimer.Elapsed += new System.Timers.ElapsedEventHandler((sender, args) => { NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log); logEx.Flush(); });
            //this.updateCameraTimer.Interval = 1000;
            //获取配置文件中刷新设备列表超时时间（s）
            if (!uint.TryParse(ConfigSettings.RefrshDeviceListOverTime, out refreshDeviceListOverTime))
            {
                //缺省为180秒钟
                refreshDeviceListOverTime = 180;
            }

            //获取配置文件中设备列表未被使用超时时间（s）
            if (!uint.TryParse(ConfigSettings.DeviceListUnusedOverTime, out deviceListUnusedOverTime))
            {
                //缺省为5秒钟
                deviceListUnusedOverTime = 5;
            }

            monitorManageServiceGetCameraList.AutoReset = true;
            monitorManageServiceGetCameraList.Elapsed += new System.Timers.ElapsedEventHandler(monitorManageServiceGetCameraList_Elapsed);
            monitorManageServiceGetCameraList.Interval = deviceListUnusedOverTime * 1000;
            monitorManageServiceGetCameraList.Start();
        }

        void monitorManageServiceGetCameraList_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            isGetDevicesFinish = true;
        }

        /// <summary>
        /// 开始连接、注册Sip服务器
        /// </summary>
        private void StartConnectRegisterSip(string domain, int sipPort, int localPort, string username, string password, string localID, string serverID, string pServerSipAccount, string pServerSipPasswd)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.StartConnectRegisterSip().");
            try
            {
                //初始化SIP
                sipStack.SIP_SDK_Init(username, password, localID, localPort, pServerSipAccount, pServerSipPasswd, serverID, domain, sipPort, OnReceivedAllDevice);
                //注册Sip服务器
                sipStack.SIP_SDK_REGISTER();
                //开始保活
                sipStack.StartKeepalive(serverID, localID);
                //设置实况rtp数据包回调
                sipStack.SetNetDataCallBack(realPlayCallback, netExceptionCallBack);

                //初始化rtp转码模块
                rtpAdapter.ESDK_RTP_Init();

                //设置rtp转码回调函数
                frameDataCallBack = FrameDataCallBackFun;
            }
            catch (System.Exception ex)
            {
                logEx.Error("T28181 StartConnectRegisterSip failed.Execption message:{0}.", ex.Message);
            }
        }

        /// <summary>
        /// 查询设备列表结束事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnReceivedAllDevice(object sender, EventArgs args)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.OnReceivedAllDevice().");

            //将实时获取的值放到缓存
            try
            {
                //拷贝devicelist到cameralist
                logEx.Debug("OnReceivedAllDevice.DeviceList.Count = {0}", sipStack.DeviceList.Count);
                GetCameraList(sipStack.DeviceList);
            }
            catch (Exception ex)
            {
                sipStack.isRefreshSucess = false;
                logEx.Error("OnReceivedAllDevice failed.  {0}", ex.Message);
            }
            finally
            {
                //查询结束
                getDeviceEndFlg = true;
            }
            logEx.Trace("Leave: T28181VideoMonitor.OnReceivedAllDevice().");
        }

        /// <summary>
        /// 获取摄像机列表、组列表、组关系列表
        /// </summary>
        /// <param name="deviceList">设备列表</param>
        /// <returns>camera列表</returns>
        private void GetCameraList(List<DeviceItem> deviceList)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.GetCameraList().");

            try
            {
                List<Camera> cameraListTemp = new List<Camera>();
                List<CameraGroup> groupListTemp = new List<CameraGroup>();
                List<NodeRelation> nodeRelationListTemp = new List<NodeRelation>();

                //过滤设备列表，获取摄像机和目录列表
                FilterDeviceList(deviceList, ref cameraListTemp, ref groupListTemp);

                //查询结果为空
                if (cameraListTemp == null || cameraListTemp.Count == 0)
                {
                    //清除缓存数据
                    ClearCamera();
                    return;
                }

                //获取摄像头和组之间的关联
                GetCameraAndGroupRelation(cameraListTemp, groupListTemp, nodeRelationListTemp);
            }
            catch (System.Exception ex)
            {
                logEx.Error("GetCameraList failed. {0} ", ex.Message);
            }
        }

        /// <summary>
        /// 获取摄像头和组之间的关联
        /// </summary>
        /// <param name="cameraListTemp">摄像机列表</param>
        /// <param name="groupListTemp">分组列表</param>
        /// <param name="nodeRelationListTemp">组关系列表</param>
        private void GetCameraAndGroupRelation(List<Camera> cameraListTemp, List<CameraGroup> groupListTemp, List<NodeRelation> nodeRelationListTemp)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.GetCameraAndGroupRelation().");

            try
            {
                if (platformType == "huawei")
                {
                    //查询摄像机的父节点,保存摄像机父节点关系列表
                    foreach (Camera ca in cameraListTemp)
                    {
                        //IVS 摄像机子设备通过主设备跟父节点关联，子设备没有父节点
                        if (ca.DeviceType == CgwConst.RESOURCE_TYPE_CAMERA)
                        {
                            continue;
                        }
                        //摄像机没有父节点
                        if (string.IsNullOrEmpty(ca.ParentID))
                        {
                            NodeRelation nodeRelation = new NodeRelation(ca.No, new List<String>(), NodeType.CAMERA);
                            nodeRelationListTemp.Add(nodeRelation);
                        }
                        else
                        {
                            string parentID = ca.ParentID;
                            //获取所有父节点路径
                            List<string> pathList = new List<string>();
                            FindNodeRelationPath(parentID, groupListTemp, ref pathList);

                            if (pathList.Count > 1)
                            {
                                //按照从顶到底排序
                                pathList.Reverse();
                            }

                            //查询主设备的子设备
                            Camera camera = cameraListTemp.Find((x)
                              =>
                            {
                                if (x.ParentID == ca.No)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }
                            });

                            if (camera != null)
                            {
                                //节点关系列表中将摄像机代替摄像机主设备
                                NodeRelation nodeRelation = new NodeRelation(camera.No, pathList, NodeType.CAMERA);
                                nodeRelationListTemp.Add(nodeRelation);
                            }
                        }
                    }

                    //设备列表过滤掉摄像机主设备
                    cameraListTemp.RemoveAll((x)
                        =>
                    {
                        if (x.DeviceType == CgwConst.RESOURCE_TYPE_MAIN)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    });

                    //查询设备组的父节点,保存设备组父节点关系列表
                    foreach (CameraGroup cg in groupListTemp)
                    {
                        //设备组没有父节点
                        if (string.IsNullOrEmpty(cg.ParentID))
                        {
                            NodeRelation nodeRelation = new NodeRelation(cg.No, new List<String>(), NodeType.GROUP);
                            nodeRelationListTemp.Add(nodeRelation);
                        }
                        else
                        {
                            string parentID = cg.ParentID;
                            //获取分组所有父节点路径
                            List<string> pathList = new List<string>();
                            FindNodeRelationPath(parentID, groupListTemp, ref pathList);

                            if (pathList.Count > 1)
                            {
                                //按照从顶到底排序
                                pathList.Reverse();
                            }
                            //保存分组的父节点列表
                            NodeRelation nodeRelation = new NodeRelation(cg.No, pathList, NodeType.GROUP);
                            nodeRelationListTemp.Add(nodeRelation);
                        }
                    }
                }
                else
                {
                    //查询摄像机的父节点,保存摄像机父节点关系列表
                    foreach (Camera ca in cameraListTemp)
                    {
                        //hikvision 摄像机父节点是设备分组
                        if (string.IsNullOrEmpty(ca.ParentID))
                        {
                            NodeRelation nodeRelation = new NodeRelation(ca.No, new List<String>(), NodeType.CAMERA);
                            nodeRelationListTemp.Add(nodeRelation);
                        }
                        else
                        {
                            //查询 是否存在父节点
                            CameraGroup cameraGroup = groupListTemp.Find((x)
                              =>
                            {
                                if (x.No == ca.ParentID)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }
                            });

                            if (cameraGroup == null)
                            {
                                //父节点不存在,把摄像机挂在根节点下
                                NodeRelation nodeRelation = new NodeRelation(ca.No, new List<String>(), NodeType.CAMERA);
                                nodeRelationListTemp.Add(nodeRelation);
                            }
                            else
                            {
                                string parentID = ca.ParentID;
                                //获取所有父节点路径
                                List<string> pathList = new List<string>();
                                FindNodeRelationPath(parentID, groupListTemp, ref pathList);

                                if (pathList.Count > 1)
                                {
                                    //按照从顶到底排序
                                    pathList.Reverse();
                                }

                                //保存分组的父节点列表
                                NodeRelation nodeRelation = new NodeRelation(ca.No, pathList, NodeType.CAMERA);
                                nodeRelationListTemp.Add(nodeRelation);
                            }
                        }
                    }
                    foreach (CameraGroup cg in groupListTemp)
                    {
                        //设备组没有父节点
                        if (string.IsNullOrEmpty(cg.ParentID))
                        {
                            NodeRelation nodeRelation = new NodeRelation(cg.No, new List<String>(), NodeType.GROUP);
                            nodeRelationListTemp.Add(nodeRelation);
                        }
                        else
                        {
                            //查询 是否存在父节点
                            CameraGroup cgTemp = groupListTemp.Find((x)
                              =>
                            {
                                if (x.No == cg.ParentID)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }
                            });

                            if (cgTemp == null)
                            {
                                //父节点不存在,把摄像机挂在根节点下
                                NodeRelation nodeRelation = new NodeRelation(cg.No, new List<String>(), NodeType.GROUP);
                                nodeRelationListTemp.Add(nodeRelation);
                            }
                            else
                            {
                                string parentID = cg.ParentID;
                                //获取分组所有父节点路径
                                List<string> pathList = new List<string>();
                                FindNodeRelationPath(parentID, groupListTemp, ref pathList);

                                if (pathList.Count > 1)
                                {
                                    //按照从顶到底排序
                                    pathList.Reverse();
                                }
                                //保存分组的父节点列表
                                NodeRelation nodeRelation = new NodeRelation(cg.No, pathList, NodeType.GROUP);
                                nodeRelationListTemp.Add(nodeRelation);
                            }
                        }
                    }
                }
                DateTime dtStart = DateTime.Now;
                DateTime dtNow = new DateTime();
                while (!isGetDevicesFinish)
                {
                    dtNow = DateTime.Now;

                    if ((dtNow - dtStart).TotalSeconds > refreshDeviceListOverTime)
                    {
                        sipStack.isRefreshSucess = false;
                        return;
                    }
                    Thread.Sleep(1);
                    continue;
                }

                //将实时获取的值放到缓存
                if (this.cameraOperateLock.TryEnterWriteLock(CgwConst.ENTER_LOCK_WAIT_TIME))
                {
                    try
                    {
                        this.cameraList = cameraListTemp;
                        this.groupList = groupListTemp;
                        this.nodeRelationList = nodeRelationListTemp;
                        sipStack.isRefreshSucess = true;
                    }
                    catch (Exception ex)
                    {
                        sipStack.isRefreshSucess = false;
                        logEx.Error("Set the list to the buffer failed. ", ex.Message);
                    }
                    finally
                    {
                        this.cameraOperateLock.ExitWriteLock();
                    }
                }
            }
            catch (System.Exception ex)
            {
                sipStack.isRefreshSucess = false;
                logEx.Error("GetCameraAndGroupRelation failed. {0} ", ex.Message);
            }
        }

        /// <summary>
        /// 获取所有父节点路径
        /// </summary>
        /// <param name="parentID">父节点ID</param>
        /// <param name="groupListTemp">组列表</param>
        /// <param name="pathList">返回父节点路径</param>
        private void FindNodeRelationPath(string parentID, List<CameraGroup> groupListTemp, ref List<string> pathList)
        {
            string newParentID = string.Empty;
            bool exists = groupListTemp.Exists((x)
                =>
                {
                    if (x.No == parentID)
                    {
                        //保存新的父节点
                        newParentID = x.ParentID;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                });
            if (exists == false)
            {
                return;
            }
            else
            {
                //增加父节点路径
                pathList.Add(parentID);

                //开始迭代查询父节点
                FindNodeRelationPath(newParentID, groupListTemp, ref  pathList);
            }
        }
        /// <summary>
        /// 过滤设备列表，获取摄像机和目录列表
        /// </summary>
        /// <param name="deviceList">输入设备列表</param>
        /// <param name="cameraListTemp">返回摄像机列表</param>
        /// <param name="groupListTemp">返回组列表</param>
        private void FilterDeviceList(List<DeviceItem> deviceList, ref List<Camera> cameraListTemp, ref List<CameraGroup> groupListTemp)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.FilterDeviceList().");

            try
            {

                foreach (DeviceItem item in deviceList)
                {
                    //根据设备ID获取设备类型
                    string devType = GetResourceType(item.DeviceID);
                    logEx.Debug("FilterDeviceList.devType = {0}", devType);
                    //共享摄像机类型、共享主设备类型,需要通过主设备来查找设备的父节点
                    if (devType == CgwConst.RESOURCE_TYPE_CAMERA || devType == CgwConst.RESOURCE_TYPE_MAIN)
                    {
                        Camera camera = new Camera(item.DeviceID, item.Name);
                        camera.Status = item.Status == "ON" ? CameraStatus.Online : CameraStatus.Offline;
                        camera.ParentID = item.ParentID;
                        camera.DeviceType = devType;
                        cameraListTemp.Add(camera);
                    }
                    //共享目录类型
                    else if (devType == CgwConst.RESOURCE_TYPE_DIR)
                    {
                        CameraGroup group = new CameraGroup(item.DeviceID, item.Name);
                        group.ParentID = item.ParentID;
                        groupListTemp.Add(group);
                    }
                }
            }
            catch (System.Exception ex)
            {
                logEx.Error("FilterDeviceList failed. {0} ", ex.Message);
            }
        }

        /// <summary>
        /// 获取资源类型
        /// </summary>
        /// <param name="deviceId">设备编码</param>
        /// <returns>设备类型</returns>
        public string GetResourceType(string deviceId)
        {
            String gDevTypeValue = "";

            if (deviceId.Length < 20)
            {
                gDevTypeValue = CgwConst.RESOURCE_TYPE_DIR;
                return gDevTypeValue;
            }
            int subDevType;
            bool TempBool = int.TryParse(deviceId.Substring(10, 3), out subDevType); //先判断是否可转换为int，否则类型设置为CgwConst.RESOURCE_TYPE_CAMERA。
            if (!TempBool)
            {
                gDevTypeValue = CgwConst.RESOURCE_TYPE_CAMERA;
                return gDevTypeValue;
            }

            if (subDevType >= CgwConst.DEVICE_TYPE_MAIN_START
                && subDevType <= CgwConst.DEVICE_TYPE_MAIN_END)
            {
                if (platformType == "huawei")
                {
                    gDevTypeValue = CgwConst.RESOURCE_TYPE_MAIN;
                }
                else
                {
                    gDevTypeValue = CgwConst.RESOURCE_TYPE_DIR;
                }
            }
            else if (subDevType == CgwConst.DEVICE_TYPE_CAMERA
                || subDevType == CgwConst.DEVICE_TYPE_NET_CAMERA)
            {
                gDevTypeValue = CgwConst.RESOURCE_TYPE_CAMERA;
            }
            else if (subDevType == CgwConst.DEVICE_TYPE_ALARM_IN)
            {
                gDevTypeValue = CgwConst.RESOURCE_TYPE_ALARM_IN;
            }
            else if (subDevType == CgwConst.DEVICE_TYPE_ALARM_OUT)
            {
                gDevTypeValue = CgwConst.RESOURCE_TYPE_ALARM_OUT;
            }
            else if (subDevType >= CgwConst.DEVICE_TYPE_PLAT_DEV_START
                && subDevType <= CgwConst.DEVICE_TYPE_PLAT_DEV_END)
            {
                gDevTypeValue = CgwConst.RESOURCE_TYPE_PLAT_DEV;
            }
            else if (subDevType == CgwConst.DEVICE_TYPE_ORG)
            {
                gDevTypeValue = CgwConst.RESOURCE_TYPE_DIR;
            }

            return gDevTypeValue;
        }

        /// <summary>
        /// 初始化T28181监控平台
        /// </summary>
        /// <param name="monitorConfigElement">监控平台配置节点</param>
        /// <returns></returns>
        public SmcErr Load(System.Xml.XmlElement monitorConfigElement)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Info("Enter: T28181VideoMonitor.Load().");
            SmcErr err = new CgwError();

            try
            {
                //解析xml节点，获取所需参数    
                string queryDeviceTimeOut = string.Empty;
                monitorId = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.ID_TAG);
                domain = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.SERVERIP_TAG);
                sipPort = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.SIP_PORT);
                deviceID = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.Device_ID);
                username = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.USER_TAG);
                //password = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.PASSWORD_TAG);
                pwdByte = CommonFunction.EncryptStr2Byte(CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.PASSWORD_TAG),CgwConst.PASSWORD_TAG);

                localPort = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.LOCAL_PORT);
                queryDeviceTimeOut = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.QueryDeviceTimeOut);
                platformType = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.Platform_Type);
                localSignalGateway = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.LOCAL_SIGNAL_GATEWAY);
                serverSignalGateway = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.SERVER_SIGNAL_GATEWAY);
                serverSipAccount = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.SERVER_SIP_ACCOUNT);
                //登陆T28181监控平台的sip加密密码
                string serverSipPasswd = CommonFunction.GetSingleNodeValue(monitorConfigElement, CgwConst.SERVER_SIP_PASSWD);

                //检测配置文件是否有错误
                int iSipPort = 0;
                bool bRet = int.TryParse(sipPort, out iSipPort);

                if (bRet == false)
                {
                    err.SetErrorNo(CgwError.MONITOR_CONFIG_FILE_INVALID);
                    logEx.Error("Load T28181 monitor failed.Execption sipPort:{0}.", sipPort);
                    return err;
                }

                int iLocalPort = 0;
                bRet = int.TryParse(localPort, out iLocalPort);

                if (bRet == false)
                {
                    err.SetErrorNo(CgwError.MONITOR_CONFIG_FILE_INVALID);
                    logEx.Error("Load T28181 monitor failed.Execption localPort:{0}.", localPort);
                    return err;
                }

                bRet = int.TryParse(queryDeviceTimeOut, out iQueryDeviceTimeOut);
                //转为毫秒
                iQueryDeviceTimeOut *= 1000;

                if (bRet == false)
                {
                    err.SetErrorNo(CgwError.MONITOR_CONFIG_FILE_INVALID);
                    logEx.Error("Load T28181 monitor failed.Execption QueryDeviceTimeOut:{0}.", iQueryDeviceTimeOut);
                    return err;
                }

                //开始连接、注册Sip服务器
                if (platformType == "huawei")
                {
                    serverSipAccount = "";
                    serverSipPasswd = "";
                }
                StartConnectRegisterSip(domain, iSipPort, iLocalPort, username, CommonFunction.DecryptByte2Str(pwdByte,CgwConst.PASSWORD_TAG), localSignalGateway, serverSignalGateway, serverSipAccount, serverSipPasswd);

                //开始查询设备列表
                Thread th = new Thread(new ThreadStart(()
                    =>
                    {
                        GetAllCamerasTimer(null, null);
                    }));
                th.Start();
                //启动定时器
                updateCameraTimer.Start();
            }
            catch (Exception e)
            {
                err.SetErrorNo(CgwError.MONITOR_CONFIG_FILE_INVALID);
                logEx.Error("Load T28181 monitor failed.Execption message:{0}.", e.Message);
                return err;
            }

            logEx.Info("Load T28181 monitor success.Monitor id:{0}.", this.monitorId);
            return err;
        }

        /// <summary>
        /// 异常回调函数
        /// </summary>
        /// <param name="ulChannel">通道号</param>
        /// <param name="iMsgType">异常消息类型</param>
        /// <param name="pParam">对应异常的信息，可扩展为结构体指针</param>
        /// <param name="pUser">用户数据</param>
        static private void NetExceptionCallBackFun(UInt32 ulChannel, UInt32 iMsgType, IntPtr pParam, IntPtr pUser)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Error("NetExceptionCallBack error, ulChannel = {0}", ulChannel);
        }

        /// <summary>
        /// T28181实况回调，获取得到实况的RTP包、处理获取到的rtp数据包
        /// </summary>
        /// <param name="pEventBuf">RTP字节数组包</param>
        /// <param name="uiSize">RTP包的大小</param>
        /// <param name="pUser">用户数据,存储rtp收流通道</param>
        static private void RealPlayCallBackRawFun(IntPtr pEventBuf, UInt32 uiSize, IntPtr pUser)
        {
            try
            {
                //获取播放通道
                int[] pChannel = new int[1];
                Marshal.Copy(pUser, pChannel, 0, 1);
                uint channel = (uint)pChannel[0];

                //进行rtp包转码
                rtpAdapter.ESDK_RTP_ProcessPacket(pEventBuf, uiSize, channel);
            }
            catch (System.Exception ex)
            {
                NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
                logEx.Error("RealPlayCallBackRawFun failed.Execption message:{0}", ex.Message);
            }
        }

        /// <summary>
        /// rtp码流回调处理
        /// </summary>
        /// <param name="pBuf">帧数据字节数组</param>
        /// <param name="pFrameData">帧数据类型</param>
        /// <param name="uiChannel">通道</param>
        /// <param name="uiBufSize">帧数据字节数组长度</param>
        private void FrameDataCallBackFun(IntPtr pBuf, uint uiBufSize, ref ST_FRAME_DATA pFrameData, uint uiChannel)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            ST_FRAME_DATA frameData = pFrameData;

            MediaDataSender mediaDataSender = null;
            if (this.handelOperateLock.TryEnterReadLock(CgwConst.ENTER_LOCK_WAIT_TIME))
            {
                try
                {
                    if (this.videoChannelDataSenderDic.ContainsKey(uiChannel))
                    {
                        mediaDataSender = this.videoChannelDataSenderDic[uiChannel];
                    }
                }
                finally
                {
                    this.handelOperateLock.ExitReadLock();
                }
            }

            if (mediaDataSender == null)
            {
                logEx.Warn("FrameDataCallBackFun mediaDataSender = NULL");
                return;
            }

            StreamType streamType = StreamType.VIDEO_H264;
            //对于支持的码流类型，用break退出switch，对于不支持的码流类型直接舍弃，用return返回
            switch (frameData.iStreamType)
            {
                //对于音频只接收G711A和G711U，其他舍弃
                case (int)IvsStreamType.PAY_LOAD_TYPE_PCMU:
                    streamType = StreamType.AUDIO_G711U;
                    break;
                case (int)IvsStreamType.PAY_LOAD_TYPE_PCMA:
                    streamType = StreamType.AUDIO_G711A;
                    break;

                //只接收H264的视频码流
                case (int)IvsStreamType.PAY_LOAD_TYPE_H264:
                    //H264的标准视频流，作为视频流处理
                    streamType = StreamType.VIDEO_H264;
                    break;
                default:
                    //不支持的类型,直接舍弃，返回
                    logEx.Warn("FrameDataCallBackFun iStreamType is not right");
                    return;
            }

            if (streamType == StreamType.AUDIO_G711A || streamType == StreamType.AUDIO_G711U)
            {
                //如果是音频流，需要判断mic的状态，开启时才发送音频流
                if (this.micOperateLock.TryEnterReadLock(CgwConst.ENTER_LOCK_WAIT_TIME))
                {
                    try
                    {
                        if (this.cameraMicStatusDic.ContainsKey(mediaDataSender.CameraNo))
                        {
                            //如果mic为非开启状态，则不发送音频码流,
                            if (!this.cameraMicStatusDic[mediaDataSender.CameraNo])
                            {
                                //logEx.Warn("This data is audio,but the mic is off.Chuck the data.Camera no:{0}", mediaDataSender.CameraNo);
                                return;
                            }
                        }
                        else
                        {
                            //默认为关闭状态，因此如果cameraMicStatusDic不包含该摄像头，则认为处于关闭状态，舍弃音频码流
                            //logEx.Warn("This data is audio,but the mic is off.Chuck the data.Camera no:{0}", mediaDataSender.CameraNo);
                            return;
                        }
                    }
                    finally
                    {
                        this.micOperateLock.ExitReadLock();
                    }
                }
            }

            try
            {
                MediaData mediaData = new MediaData();

                //获取非托管的数据 
                byte[] datagram = new byte[uiBufSize];
                Marshal.Copy(pBuf, datagram, 0, (int)uiBufSize);

                //视频数据增加头信息
                if (!(streamType == StreamType.AUDIO_G711A || streamType == StreamType.AUDIO_G711U))
                {
                    //头部增加四个四节的开始表实0x000001
                    byte[] newDatagram = new byte[uiBufSize + 4];
                    datagram.CopyTo(newDatagram, 4);
                    newDatagram[3] = 1;
                    mediaData.Data = newDatagram;
                    mediaData.Size = (uint)(uiBufSize + 4);
                }
                else
                {
                    mediaData.Data = datagram;
                    mediaData.Size = (uint)(uiBufSize);
                }
                //裸码流
                mediaData.DataType = MediaDataType.FRAME_DATA;
                mediaData.StreamType = streamType;


                //将帧类型转换成各融合网关统一的帧类型
                string name = Enum.GetName(typeof(IvsH264NaluType), frameData.iFrameDataType);
                if (Enum.IsDefined(typeof(FrameDataType), name))
                {
                    FrameDataType frameDataType = (FrameDataType)Enum.Parse(typeof(FrameDataType), name);
                    mediaData.FrameType = frameDataType;
                }
                else
                {
                    mediaData.FrameType = FrameDataType.H264_NALU_TYPE_UNDEFINED;
                    logEx.Warn("T28181 FrameDataCallBackFun FrameType is Not Defined, FrameType:{0}", frameData.iFrameDataType);
                }

                //logEx.Debug("FrameDataCallBackFun.mediaData.DataType={0},FrameType = {1},StreamType = {2},Size = {3}", Enum.GetName(typeof(MediaDataType), mediaData.DataType),
                //Enum.GetName(typeof(FrameDataType), mediaData.FrameType), Enum.GetName(typeof(StreamType), mediaData.StreamType), mediaData.Size);
                //向回调函数转发码流
                mediaDataSender.SendData(mediaData, this.sender);
            }
            catch (System.Exception ex)
            {
                logEx.Error("FrameDataCallBackFun failed.Execption message:{0}", ex.Message);
            }
        }

        /// <summary>
        /// 注销T28181监控平台资源
        /// </summary>
        /// <returns></returns>
        public SmcErr Unload()
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Info("Enter: T28181IvsVideoMonitor.Unload().");
            SmcErr err = new CgwError();
            //停止定时器
            this.updateCameraTimer.Stop();

            //copy 一份，防止长时间占用锁
            Dictionary<string, uint> monitorChannelRelationDicTemp = null;
            try
            {
                monitorChannelRelationDicTemp = new Dictionary<string, uint>(cameraVideoChannelDic);
            }
            catch (Exception e)
            {
                logEx.Error("Unload.Execption message:{0}", e.Message);
            }

            if (monitorChannelRelationDicTemp == null)
            {
                //记录日志，获取*监控平台的摄像头列表失败
                logEx.Error("Unload failed.No any cameraVideoChannelDic.");
                return err;
            }

            //遍历通道字典，停止流
            foreach (KeyValuePair<string, uint> videoChannelRelation in monitorChannelRelationDicTemp)
            {
                string cameraNo = videoChannelRelation.Key.ToString();
                string channel = videoChannelRelation.Value.ToString();
                if (!string.IsNullOrEmpty(cameraNo))
                {
                    SmcErr errs = this.StopReceiveVideo(cameraNo);
                    if (!errs.IsSuccess())
                    {
                        logEx.Error("@@Unload.StopReceiveVideo failed, cameraNo:{0},errNo={1}.", cameraNo, errs.ErrNo);
                    }
                    else
                    {
                        logEx.Trace("@@Unload.StopReceiveVideo success, cameraNo:{0} ", cameraNo);
                    }
                }
                else
                {
                    logEx.Error("@@Unload.StopReceiveVideo failed, cameraNo is null.");
                }
            }
            logEx.Trace("Leave: T28181IvsVideoMonitor.Unload().");
            EM_SIP_RESULT iRet = sipStack.SIP_SDK_UNREGISTER();
            //释放所有实况通道,释放NETSOURCE资源
            IVS_NETSOURCE_RESULT iNet = sipStack.IVS_NETSOURCE_UnInit();
            iRet += (int)sipStack.SIP_SDK_UnInit();
            iNet += rtpAdapter.ESDK_RTP_UnInit();

            logEx.Info("Unload T28181video.Monitor id:{0},iRet:{1} ,iNet:{2}", this.monitorId, iRet, iNet);
            //if (iRet == EM_SIP_RESULT.RET_SUCCESS || iNet != IVS_NETSOURCE_RESULT.SUCCESS)
            if (iRet == EM_SIP_RESULT.RET_SUCCESS && iNet == IVS_NETSOURCE_RESULT.SUCCESS)
            {
                logEx.Info("Unload T28181video monitor success.Monitor id:{0}.", this.monitorId);
            }
            else
            {
                err.SetErrorNo(CgwError.MONITOR_UDLOAD_FAILED);
                logEx.Error("Unload T28181video monitor failed.Monitor id:{0}.", this.monitorId);
            }
            return err;
        }

        /// <summary>
        /// 注册码流回调函数
        /// </summary>
        /// <param name="dataCallBack">回调函数</param>
        /// <param name="sender">调用者</param>
        /// <returns></returns>
        public void SetDataCallBackFunc(DataCallBack dataCallBack, string sender)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.SetVideoDataCallBackFunc().");
            this.dataCallBack = dataCallBack;
            this.sender = sender;
            logEx.Info("Set VideoDataCallBackFunc success. Monitor id:{0}", this.monitorId);
        }

        /// <summary>
        /// 获取摄像头列表及分组信息
        /// </summary>
        /// <param name="isRealTime">是否实时获取，融合网关有个缓存，间隔一段时间获取，默认是从融合网关获取列表，如果该值为true，则实时获取</param>
        /// <param name="cameraList">摄像头列表</param>
        /// <param name="groupList">组信息</param>
        /// <param name="nodeRelationList">分组关系</param>
        /// <returns></returns>
        public SmcErr GetAllCameras(out List<Camera> cameraList, out List<CameraGroup> groupList, out List<NodeRelation> nodeRelationList)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.GetAllCameras().");

            SmcErr err = new CgwError();
            cameraList = new List<Camera>();
            groupList = new List<CameraGroup>();
            nodeRelationList = new List<NodeRelation>();

            if (this.cameraOperateLock.TryEnterReadLock(CgwConst.ENTER_LOCK_WAIT_TIME))
            {
                try
                {
                    #region 深度克隆数据
                    foreach (Camera ivsCamera in this.cameraList)
                    {
                        //从缓存获取                        
                        Camera camera = new Camera(ivsCamera.No, ivsCamera.Name);
                        camera.Status = ivsCamera.Status;
                        cameraList.Add(camera);
                    }
                    foreach (CameraGroup cameraGroup in this.groupList)
                    {
                        CameraGroup cameraGroupTemp = new CameraGroup(cameraGroup.No, cameraGroup.Name);
                        groupList.Add(cameraGroupTemp);
                    }
                    foreach (NodeRelation nodeRelation in this.nodeRelationList)
                    {
                        NodeRelation nodeRelationTemp = new NodeRelation(nodeRelation.No, nodeRelation.Path, nodeRelation.Type);
                        nodeRelationList.Add(nodeRelationTemp);
                    }
                    #endregion
                }
                catch (Exception e)
                {
                    err.SetErrorNo(CgwError.GET_ALL_CAMERAS_FAILED);
                    logEx.Error("Get all cameras failed.Execption message:{0}", e.Message);
                    return err;
                }
                finally
                {
                    this.cameraOperateLock.ExitReadLock();
                }
            }
            logEx.Debug("cameraList.{0}", cameraList.Count);
            logEx.Debug("groupList.{0}", groupList.Count);
            logEx.Debug("nodeRelationList.{0}", nodeRelationList.Count);
            logEx.Debug("Get all cameras success.");
            return err;
        }

        /// <summary>
        /// 启动摄像头预览
        /// </summary>
        /// <param name="cameraNo">摄像头编号</param>
        /// <returns></returns>
        public SmcErr StartReceiveVideo(string cameraNo)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.StartReceiveVideo({0}).", cameraNo);
            SmcErr err = new CgwError();

            //打开通道，开始接收实况RTP数据流
            UInt32 channel = sipStack.StartRecvStream(cameraNo, domain, sipPort, localPort);

            //如果为0，表示预览失败
            if (channel == CgwConst.T28181_ERROR_HANDLE)
            {
                err.SetErrorNo(CgwError.START_RECEIVE_VIDEO_FAILED);
                logEx.Error("Start Receive camera video data failed.Camera No:{0}.Handle:{1}.", cameraNo, channel);
                return err;
            }
            else
            {
                logEx.Info("Start Receive camera video data success.Camera No:{0},Handle:{1}.", cameraNo, channel);
            }

            //设置rtp解析回调函数
            rtpAdapter.ESDK_RTP_OpenChannel(frameDataCallBack, channel);

            //预览成功，需要停止原来的预览，并将预览句柄添加到缓存
            //需要停止的预览句柄
            UInt32 needToStopChannel = CgwConst.T28181_ERROR_HANDLE;
            if (this.handelOperateLock.TryEnterWriteLock(CgwConst.ENTER_LOCK_WAIT_TIME))
            {
                try
                {
                    //如果预览句柄已经存在，删除掉原来的句柄,用新的句柄替换
                    if (this.cameraVideoChannelDic.ContainsKey(cameraNo))
                    {
                        needToStopChannel = this.cameraVideoChannelDic[cameraNo];
                        this.videoChannelDataSenderDic.Remove(needToStopChannel);
                        this.cameraVideoChannelDic.Remove(cameraNo);

                        //用户参数,4字节整数
                        IntPtr pUser = Marshal.AllocHGlobal(4);
                        NetSourcedInterface.IVS_NETSOURCE_SetDataCallBack(needToStopChannel, null, pUser);

                        //释放NETSOURCE通道资源
                        IVS_NETSOURCE_RESULT iNet = NetSourcedInterface.IVS_NETSOURCE_CloseNetStream(needToStopChannel);
                        if (iNet != IVS_NETSOURCE_RESULT.SUCCESS)
                        {
                            logEx.Error("IVS_NETSOURCE_CloseNetStream failed channel={0}", needToStopChannel);
                            err.SetErrorNo(CgwError.STOP_RECEIVE_VIDEO_FAILED);
                        }

                        //关闭rtp回调
                        rtpAdapter.ESDK_RTP_CloseChannel(needToStopChannel);
                    }
                    this.cameraVideoChannelDic.Add(cameraNo, channel);
                    MediaDataSender mediaDataSender = new MediaDataSender(cameraNo, this.dataCallBack);
                    this.videoChannelDataSenderDic.Add(channel, mediaDataSender);
                }
                finally
                {
                    this.handelOperateLock.ExitWriteLock();
                }
            }

            //重新预览后，更新了预览句柄，需要将原来的预览停止，放在handelOperateLock外面，防止长时间占用锁
            if (needToStopChannel != CgwConst.T28181_ERROR_HANDLE)
            {
                EM_SIP_RESULT iRet = sipStack.StopRecvStream(needToStopChannel);
                //如果不为0，表示停止原来的预览失败，只记录日志，不返回错误，不设置错误码
                if (iRet != EM_SIP_RESULT.RET_SUCCESS)
                {
                    err.SetErrorNo(CgwError.START_RECEIVE_VIDEO_FAILED);
                    logEx.Error("Get a new preview success. But stop old preview failed.CameraNo:{0},Ivs sdk error code:{0}", cameraNo, iRet);
                    return err;
                }
            }
            return err;
        }

        /// <summary>
        /// 停止预览
        /// </summary>
        /// <param name="cameraNo">摄像头编号</param>
        /// <returns>成功返回0，失败返回错误码</returns>
        public SmcErr StopReceiveVideo(string cameraNo)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.StopReceiveVideo({0}).", cameraNo);
            SmcErr err = new CgwError();
            //需要停止的预览句柄
            uint needToStopChannel = 0;
            if (this.handelOperateLock.TryEnterWriteLock(CgwConst.ENTER_LOCK_WAIT_TIME))
            {
                try
                {
                    if (this.cameraVideoChannelDic.ContainsKey(cameraNo))
                    {
                        needToStopChannel = this.cameraVideoChannelDic[cameraNo];
                        this.videoChannelDataSenderDic.Remove(needToStopChannel);
                        this.cameraVideoChannelDic.Remove(cameraNo);

                        //用户参数,4字节整数
                        IntPtr pUser = Marshal.AllocHGlobal(4);
                        NetSourcedInterface.IVS_NETSOURCE_SetDataCallBack(needToStopChannel, null, pUser);

                        //释放NETSOURCE通道资源
                        IVS_NETSOURCE_RESULT iNet = NetSourcedInterface.IVS_NETSOURCE_CloseNetStream(needToStopChannel);
                        if (iNet != IVS_NETSOURCE_RESULT.SUCCESS)
                        {
                            logEx.Error("IVS_NETSOURCE_CloseNetStream failed channel={0}", needToStopChannel);
                            err.SetErrorNo(CgwError.STOP_RECEIVE_VIDEO_FAILED);
                        }

                        //关闭rtp回调
                        rtpAdapter.ESDK_RTP_CloseChannel(needToStopChannel);
                    }
                    else
                    {
                        logEx.Warn("Stop Receive camera video data failed. Don't need to end the preview.Camera No:{0}.", cameraNo);
                        //如果预览句柄不存在，不需要处理，直接返回
                        return err;
                    }
                }
                catch (Exception ex)
                {
                    err.SetErrorNo(CgwError.STOP_RECEIVE_VIDEO_FAILED);
                    logEx.Error("Stop Receive camera video data failed.Execption message:{0}", ex.Message);
                    return err;
                }
                finally
                {
                    this.handelOperateLock.ExitWriteLock();
                }
            }

            //调用sdk的停止方法，放在handelOperateLock外面，防止长时间占用锁
            if (needToStopChannel != 0)
            {
                EM_SIP_RESULT iRet = sipStack.StopRecvStream(needToStopChannel);
                //如果不为0，表示预览失败
                if (iRet != EM_SIP_RESULT.RET_SUCCESS)
                {
                    err.SetErrorNo(CgwError.STOP_RECEIVE_VIDEO_FAILED);
                    logEx.Error("Stop Receive camera video data failed. error code:{0}", iRet);
                    return err;
                }
                logEx.Info("Stop Receive camera video data success.Camera No:{0},Handle:{1}.", cameraNo, needToStopChannel);
            }

            return err;
        }

        /// <summary>
        /// 开始云台控制，摄像头控制
        /// </summary>
        /// <param name="cameraNo">摄像头编号</param>
        /// <param name="ptzCommandType">命令类型</param>
        /// <param name="param">命令参数，速度或倍数</param>
        /// <returns></returns>
        public SmcErr StartControlPtz(string cameraNo, PtzCommandType ptzCommand, int param)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.StartControlPtz({0},{1},{2}).", cameraNo, ptzCommand, param);
            SmcErr err = new CgwError();

            //将ptz命令转换成T28181的命令
            T28181PTZCmd cmd = new T28181PTZCmd(ptzCommand, param);
            string ptzCmd = cmd.ToString();

            logEx.Trace("Call T28181VideoMonitor.StartPtzControl({0},{1},{2}).", cameraNo, Enum.GetName(typeof(PtzCommandType), (int)ptzCommand), param);
            //控制权限级别设为1
            EM_SIP_RESULT iRet = sipStack.PtzControl(cameraNo, ptzCmd, "1");

            //如果为0，表示成功
            if (iRet == EM_SIP_RESULT.RET_SUCCESS)
            {
                logEx.Info("Start control ptz success.Camera No:{0}.", cameraNo);
            }
            else
            {
                err.SetErrorNo(CgwError.START_CONTROL_PTZ_FAILED);
                logEx.Error("Start control ptz failed.Camera No:{0}.T28181VideoMonitor error code：{1}.", cameraNo, iRet);
                return err;
            }

            return err;
        }

        /// <summary>
        /// 停止云台控制，摄像头控制
        /// </summary>
        /// <param name="cameraNo">摄像头编号</param>
        /// <param name="ptzCommandType">命令类型</param>
        /// <returns></returns>
        public SmcErr StopControlPtz(string cameraNo, PtzCommandType ptzCommandType)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.StopControlPtz({0}).", cameraNo);

            SmcErr err = new CgwError();

            //将ptz命令转换成T28181的命令
            T28181PTZCmd cmd = new T28181PTZCmd(ptzCommandType, 0);
            string ptzCmd = cmd.ToString();

            //控制权限级别设为1
            EM_SIP_RESULT iRet = sipStack.PtzControl(cameraNo, ptzCmd, "1");
            //如果为0，表示成功
            if (iRet == EM_SIP_RESULT.RET_SUCCESS)
            {
                logEx.Info("Stop control ptz success.Camera No:{0}.", cameraNo);
            }
            else
            {
                //直接将IVS的错误码返回
                err.SetErrorNo(CgwError.STOP_CONTROL_PTZ_FAILED);
                logEx.Error("Stop control ptz failed.Camera No:{0}.Ivs sdk error code：{1}.", cameraNo, iRet);
                return err;
            }
            return err;
        }

        /// <summary>
        /// 重发I帧（暂不支持）
        /// </summary>
        /// <param name="cameraNo">摄像头编号</param>
        /// <returns></returns>
        public SmcErr MakeIFrame(string cameraNo)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.MakeIFrame({0}).", cameraNo);
            SmcErr err = new CgwError();
            return err;
        }

        /// <summary>
        /// 设置扬声器状态（暂不支持）
        /// </summary>
        /// <param name="cameraNo"></param>
        /// <param name="isOn">扬声器是否开启</param>
        /// <returns></returns>
        public SmcErr SetSpeaker(string cameraNo, bool isOn)
        {
            SmcErr err = new CgwError();
            return err;
        }

        /// <summary>
        /// 设置麦克风状态，非物理状态，通过软件控制，该状态只针对该融合网关。软件重启，状态丢失
        /// </summary>
        /// <param name="cameraNo"></param>
        /// <param name="isOn">麦克风是否开启</param>
        /// <returns></returns>
        public SmcErr SetMic(string cameraNo, bool isOn)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.SetMic({0}，{1}).", cameraNo, isOn);
            SmcErr err = new CgwError();

            if (this.micOperateLock.TryEnterWriteLock(CgwConst.ENTER_LOCK_WAIT_TIME))
            {
                try
                {
                    if (this.cameraMicStatusDic.ContainsKey(cameraNo))
                    {
                        this.cameraMicStatusDic[cameraNo] = isOn;
                    }
                    else
                    {
                        this.cameraMicStatusDic.Add(cameraNo, isOn);
                    }
                }
                finally
                {
                    this.micOperateLock.ExitWriteLock();
                }
            }
            logEx.Info("Set Mic status success.Camera no:{0}，isOn:{1}).", cameraNo, isOn);
            return err;
        }

        /// <summary>
        /// 获取摄像头列表及分组信息定时器
        /// 1、获取系统中所有的域
        /// 2、循环所有的域，查询域下面的分组，递归处理，获取节点关系
        /// 3、查询设备列表
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GetAllCamerasTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (getDeviceEndFlg)
            {
                getDeviceEndFlg = false;
                //查询设备未完成，需要阻塞直到查询结束 
                System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

                NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
                logEx.Trace("Enter: T28181VideoMonitor.GetAllCamerasTimer().");

                try
                {
                    //获取设备列表
                    sipStack.GetDeviceList(serverSignalGateway, deviceID);
                    
                    //开始计时
                    stopwatch.Start();

                    //查询结束或者超时时结束等待 
                    while (!getDeviceEndFlg && stopwatch.ElapsedMilliseconds < iQueryDeviceTimeOut)
                    {
                        Thread.Sleep(CgwConst.Thread_Sleep_Time);
                    }
                    if (stopwatch.ElapsedMilliseconds >= iQueryDeviceTimeOut)
                    {
                        logEx.Warn("GetAllCamerasTimer Timeout");
                        sipStack.isRefreshSucess = false;
                    }
                }
                catch (System.Exception ex)
                {
                    logEx.Error("GetAllCamerasTimer failed.Exception message:{0}", ex.Message);
                    sipStack.isRefreshSucess = false;
                }
                finally
                {
                    //停止计时、获取设备完成标志复位
                    stopwatch.Stop();
                    getDeviceEndFlg = true;

                    logEx.Debug("cameraList.{0}", cameraList.Count);
                    logEx.Debug("groupList.{0}", groupList.Count);
                    logEx.Debug("nodeRelationList.{0}", nodeRelationList.Count);
                    logEx.Debug("Leave: T28181VideoMonitor.GetAllCamerasTimer().");
                }
            }
        }

        /// <summary>
        /// 清除缓存数据
        /// </summary>
        private void ClearCamera()
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.ClearCamera().");
            if (this.cameraOperateLock.TryEnterWriteLock(CgwConst.ENTER_LOCK_WAIT_TIME))
            {
                try
                {
                    this.cameraList = new List<Camera>();
                    this.groupList = new List<CameraGroup>();
                    this.nodeRelationList = new List<NodeRelation>();
                }
                finally
                {
                    this.cameraOperateLock.ExitWriteLock();
                }
            }
            logEx.Trace("Clear Camera which in the cache success.");
        }

        /// <summary>
        /// 刷新监控摄像头列表
        /// </summary>
        /// <returns></returns>
        public SmcErr RefreshMonitorCamera()
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.RefreshMonitorCamera");
            SmcErr err = new CgwError();

            if (getDeviceEndFlg)
            {
                GetAllCamerasMethod();
                //重新开始计时
                updateCameraTimer.Stop();
                updateCameraTimer.Start();
            }
            logEx.Info("T28181VideoMonitor.RefreshMonitorCamera success.");
            return err;
        }

        /// <summary>
        /// 获取摄像头列表及分组信息
        /// </summary>
        private void GetAllCamerasMethod()
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: T28181VideoMonitor.GetAllCamerasMethod().");
            try
            {
                GetAllCamerasTimer(null, null);
            }
            catch (System.Exception ex)
            {
                sipStack.isRefreshSucess = false;
                logEx.Error("T28181VideoMonitor.GetAllCamerasMethod failed.Exception message:{0}", ex.Message);
            }
        }

        /// <summary>
        /// 获取监控摄像头列表刷新状态，返回结果为0是表示刷新完毕，为1是刷新操作中。当查询刷新状态为0时，可调用获取监控摄像头列表接口，获取刷新后监控摄像头列表
        /// </summary>
        /// <param name="refreshStatus">刷新状态</param>
        /// <returns></returns>
        public SmcErr GetRefreshStatus(out SmcErr refreshStatus)
        {
            NLogEx.LoggerEx logEx = new NLogEx.LoggerEx(log);
            logEx.Trace("Enter: IvsVideoMonitor.GetRefreshStatus");
            SmcErr err = new CgwError();
            refreshStatus = new SmcErr();
            refreshStatus.ErrNo = CgwError.ERR_DEVICE_LIST_REFRESH_STATUS_END;

            if (getDeviceEndFlg)
            {
                refreshStatus.ErrNo = sipStack.isRefreshSucess ? CgwError.ERR_DEVICE_LIST_REFRESH_STATUS_END : CgwError.ERR_DEVICE_LIST_REFRESH_STATUS_FAILED;
            }
            else
            {
                refreshStatus.ErrNo = CgwError.ERR_DEVICE_LIST_REFRESH_STATUS_EXECUTING;
            }
            logEx.Info("GetRefreshStatus success.");
            return err;
        }
    }
}

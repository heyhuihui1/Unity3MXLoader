using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json.Linq;

/**
 * Unity3MXLoader
 * 一个加载3MX模型的Unity插件
 * @author lijuhong1981
 */
namespace Unity3MX
{
    public class Unity3MXComponent : MonoBehaviour
    {
        [Tooltip("初始加载url")]
        public string url;
        [Tooltip("主相机，不设置则使用Camera.main")]
        public Camera mainCamera;
        [Tooltip("是否随脚本启动执行")]
        public bool runOnStart = true;
        [Tooltip("执行处理逻辑时间间隔，单位秒")]
        [Min(0)]
        public float processInterval = 1.0f / 30.0f;
        [Tooltip("最大并发任务数，也可以理解为最大并发线程数")]
        [Min(1)]
        public int maxTaskCount = Environment.ProcessorCount;
        [Tooltip("直径缩放倍率，会影响当前相机视图下显示层级的选择")]
        [Min(0.1f)]
        public float diameterRatio = 1.0f;
        [Tooltip("相机fov倍率，会影响相机可见区域的范围")]
        [Min(0.1f)]
        public float fieldOfViewRatio = 1.2f;
        [Tooltip("阴影模式")]
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
        [Tooltip("失败重试次数")]
        [Min(0)]
        public int failRetryCount = 5;
        [Tooltip("是否启用DebugCollider，开启后会为每个Tile添加一个BoxCollider")]
        public bool enableDebugCollider = false;
        [Tooltip("是否开启内存缓存，开启后加载过的瓦片资源会缓存在内存里，能减少加载次数但会加大内存消耗")]
        public bool enableMemeoryCache = false;

        private float mLastTime = 0.0f;
        private CameraState mCameraState;

        private string mBaseUrl;
        public string baseUrl
        {
            get { return mBaseUrl; }
        }

        private bool mLoading = false;
        public bool isLoading
        {
            get { return mLoading; }
        }

        private bool mReady = false;
        public bool isReady
        {
            get { return mReady; }
        }

        private string mSRS;
        public string SRS
        {
            get { return mSRS; }
        }

        private double[] mSRSOrigin;
        public double[] SRSOrigin
        {
            get { return mSRSOrigin; }
        }

        private string mRootDataPath;
        public string rootDataPath
        {
            get { return mRootDataPath; }
        }

        private string mBaseDataUrl;
        public string baseDataUrl
        {
            get { return mBaseDataUrl; }
        }

        private bool mHasOffset = false;
        private Vector3 mOffset = new();
        public Vector3 offset
        {
            get { return mOffset; }
        }

        private GameObject mRootObject;
        public GameObject rootObject
        {
            get { return mRootObject; }
        }

        private List<Unity3MXRootNode> mRootNodes = new();
        public List<Unity3MXRootNode> rootNodes
        {
            get { return mRootNodes; }
        }
        private List<Unity3MXTileNode> mReadyUnloadNodes = new();

        public delegate void OnReady(Unity3MXComponent component);
        public event OnReady onReady;
        public delegate void OnError(Unity3MXComponent component, string error);
        public event OnError onError;

        internal TaskPool taskPool = new();

        // Start is called before the first frame update
        void Start()
        {
            Debug.Log("ProcessorCount: " + Environment.ProcessorCount);
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (runOnStart)
                Run();
        }

        private void OnDestroy()
        {
            taskPool.Dispose();
        }

        // Update is called once per frame
        void Update()
        {

        }

        void LateUpdate()
        {
            //计算间隔时间是否大于updateIntervalTime，是则继续往下执行
            float nowTime = Time.time;
            if (nowTime - mLastTime < processInterval)
                return;
            mLastTime = nowTime;
            //主相机不为空
            if (mainCamera != null)
            {
                //生成CameraState并执行Update
                if (mCameraState == null)
                    mCameraState = new CameraState();
                mCameraState.Update(this, mainCamera);
            }
            //未初始化或未获取到主相机，不往下执行
            if (!mReady || mCameraState == null || mRootNodes.Count == 0)
                return;
            //存在offset，设置偏移
            if (mHasOffset)
                mRootObject.transform.localPosition = mOffset;

            taskPool.MaxTasks = maxTaskCount;

            //按相机距离升序排序
            mRootNodes.Sort(compareRootNode);
            //执行node.Update
            foreach (var rootNode in mRootNodes)
            {
                rootNode.Process(mCameraState);
            }
            //test code
            //var length = Math.Min(2, mRootNodes.Count);
            //for (int i = 0; i < length; i++)
            //{
            //    mRootNodes[i].Update(mCameraState);
            //}
            //mRootNodes[1].Update(mCameraState);

            if (mReadyUnloadNodes.Count > 0)
            {
                //遍历readyUnloadNodes
                foreach (var node in mReadyUnloadNodes)
                {
                    //执行node.Unload
                    node.Unload();
                }
                //执行完后清除readyUnloadNodes
                mReadyUnloadNodes.Clear();
            }
        }

        private int compareRootNode(Unity3MXRootNode left, Unity3MXRootNode right)
		{
            var result = left.cameraDistance - right.cameraDistance;
            if (result > 0)
                return Mathf.CeilToInt(result);
            else if (result < 0)
                return Mathf.FloorToInt(result);
            return 0;
        }

        public void Run()
        {
            //已加载完成，需要先调用Clear()后才能再次加载
            if (mReady)
                return;
            //已在加载中，不能重复调用
            if (mLoading)
                return;
            //检查url是否可用
            if (UrlUtils.CheckUrl(url, (string error) =>
            {
                onError?.Invoke(this, error);
            }))
            {
                //获取baseUrl
                mBaseUrl = UrlUtils.ExtractBaseUrl(url);
                Debug.Log("Get baseUrl: " + mBaseUrl);
                //启动TaskPool
                taskPool.Start();
                //开始初始化
                StartCoroutine(initialize());
            }
        }

        //初始化
        private IEnumerator initialize()
        {
            mLoading = true;

            bool isError = false;
            //获取url指向的文本
            yield return RequestUtils.GetText(url, (string error) =>
            {
                onError?.Invoke(this, error);
                isError = true;
            }, (string text) =>
            {
                Debug.Log("Get rootJson: " + text);

                parseRootJson(text);
            });

            if (isError)
                yield break;

            yield return createRootObject();
            yield return loadRootData();
        }

        //解析Json文本
        private void parseRootJson(string jsonText)
        {
            Console.WriteLine(jsonText);
            JObject jRoot = JObject.Parse(jsonText);
            JArray jLayers = (JArray)jRoot.GetValue("layers");
            JObject jLayer = (JObject)jLayers[0];

            mSRS = (string)jLayer.GetValue("SRS");

            JArray jSRSOrigin = (JArray)jLayer.GetValue("SRSOrigin");
            mSRSOrigin = new double[jSRSOrigin.Count];
            for (int i = 0; i < jSRSOrigin.Count; i++)
            {
                mSRSOrigin[i] = (double)jSRSOrigin[i];
            }

            string rootPath = (string)jLayer.GetValue("root");
            mRootDataPath = mBaseUrl + rootPath;
            Debug.Log("Get rootDataPath: " + mRootDataPath);
            mBaseDataUrl = UrlUtils.ExtractBaseUrl(mRootDataPath);

            JToken value = jLayer.GetValue("offset");
            if (value != null)
            {
                JArray jOffset = (JArray)value;
                if (jOffset[0] != null)
                    mOffset.x = (float)jOffset[0];
                if (jOffset[1] != null)
                    mOffset.y = (float)jOffset[2];
                if (jOffset[2] != null)
                    mOffset.z = (float)jOffset[1];
                mHasOffset = true;
            }
        }

        //创建根对象
        private IEnumerator createRootObject()
        {
            mRootObject = new GameObject("Root");
            mRootObject.transform.SetParent(this.transform, false);
            yield return new WaitForEndOfFrame();
        }

        //加载根数据
        private IEnumerator loadRootData()
        {
            Unity3MXBLoader loader = new Unity3MXBLoader(this, mRootDataPath);
            loader.onLoad += (HeaderInfo header) =>
            {
                //生成根节点
                for (int i = 0; i < header.nodes.Count; i++)
                {
                    NodeInfo node = header.nodes[i];
                    mRootNodes.Add(new Unity3MXRootNode(this, node));
                }
                //初始化完成
                mLoading = false;
                mReady = true;
                Debug.Log("This 3MXComponent is ready.");
                onReady?.Invoke(this);
            };
            loader.onError += (string error) =>
            {
                onError?.Invoke(this, error);
            };
            loader.Load();
            yield return null;
        }

        public void Clear()
        {
            //加载未完成，不能调用Clear()
            if (!mReady)
                return;

            taskPool.Clear();
            taskPool.Stop();
            //StopAllCoroutines();

            foreach (var node in mRootNodes)
            {
                node.Destroy();
            }
            mRootNodes.Clear();
            mReadyUnloadNodes.Clear();
            if (mRootObject != null)
            {
                Destroy(mRootObject);
                mRootObject = null;
            }

            mReady = false;
        }

        internal void AddReadyUnloadNode(Unity3MXTileNode node)
        {
            mReadyUnloadNodes.Add(node);
        }

        //检查Node的父Node是否处于ready状态，处于则从readyUnloadNodes列表中移除
        internal void CheckAndRemoveParentNode(Unity3MXTileNode node)
        {
            var parentNode = node.tile.parentNode;
            if (parentNode != null)
            {
                //判断parentNode是否已ready，是则从readyUnloadNodes列表中移除
                if (parentNode.isReady)
                    mReadyUnloadNodes.Remove(parentNode);
                //否则继续往上递归查找
                else
                    CheckAndRemoveParentNode(parentNode);
            }
        }
    }

    public class CameraState
    {
        public Camera camera;
        private Camera mCamera;
        public Plane[] planes;
        public float fieldOfView;
        public float nearClipPlane;
        public float inverseNear;
        public float topClipPlane;
        public float rightClipPlane;

        public void Update(Unity3MXComponent rootComponent, Camera camera)
        {
            bool changed = false;
            if (mCamera != camera)
            {
                mCamera = camera;
                if (this.camera != null)
                    UnityEngine.Object.Destroy(this.camera);
                //克隆一个新相机
                this.camera = UnityEngine.Object.Instantiate(camera);
                //设置为非活动状态
                this.camera.gameObject.SetActive(false);
                changed = true;
            }
            if (this.fieldOfView != camera.fieldOfView)
            {
                this.fieldOfView = camera.fieldOfView;
                changed = true;
            }
            if (this.nearClipPlane != camera.nearClipPlane)
            {
                this.nearClipPlane = camera.nearClipPlane;
                //计算1.0÷nearClipPlane的值
                this.inverseNear = 1.0f / this.nearClipPlane;
                changed = true;
            }
            if (changed)
            {
                //计算topClipPlane
                this.topClipPlane = this.nearClipPlane * Mathf.Tan(0.5f * this.fieldOfView * Mathf.Deg2Rad);
                //计算rightClipPlane
                this.rightClipPlane = camera.aspect * this.topClipPlane;
            }
            //设置相机相关参数
            this.camera.fieldOfView = camera.fieldOfView * rootComponent.fieldOfViewRatio;
            this.camera.aspect = camera.aspect;
            this.camera.nearClipPlane = camera.nearClipPlane;
            this.camera.farClipPlane = camera.farClipPlane;
            this.camera.transform.position = camera.transform.position;
            this.camera.transform.rotation = camera.transform.rotation;
            //获取相机视锥体planes
            this.planes = GeometryUtility.CalculateFrustumPlanes(this.camera);
        }

        //测试是否可见
        public bool TestVisibile(Bounds bounds)
        {
            return GeometryUtility.TestPlanesAABB(this.planes, bounds);
        }

        //计算单位像素大小，即米/像素
        public float ComputePixelSize(Vector3 position)
        {
            float distance = Vector3.Distance(position, this.camera.transform.position);
            float tanTheta = this.topClipPlane * this.inverseNear;
            float pixelHeight = (2.0f * distance * tanTheta) / Screen.height;
            tanTheta = this.rightClipPlane * this.inverseNear;
            float pixelWidth = (2.0f * distance * tanTheta) / Screen.width;
            return Mathf.Max(pixelWidth, pixelHeight);
        }
    }

}

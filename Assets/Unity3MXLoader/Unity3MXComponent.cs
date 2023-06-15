using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json.Linq;

namespace Unity3MX
{
    public class Unity3MXComponent : MonoBehaviour
    {
        [Tooltip("��ʼ����url")]
        public string url;
        [Tooltip("���������������ʹ��Camera.main")]
        public Camera mainCamera;
        [Tooltip("�Ƿ���ű�����ִ��")]
        public bool runOnStart = true;
        [Tooltip("ִ�и���ʱ��������λ��")]
        [Min(0)]
        public float updateIntervalTime = 1.0f / 60;
        [Tooltip("ֱ�����ű���")]
        [Min(0.1f)]
        public float diameterRatio = 1.0f;
        [Tooltip("��Ӱģʽ")]
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
        [Tooltip("ʧ�����Դ���")]
        [Min(0)]
        public int failRetryCount = 5;
        [Tooltip("�Ƿ����ڴ滺�棬��������ع�����Ƭ��Դ�Ỻ�����ڴ���ܼ��ټ��ش�������Ӵ��ڴ�����")]
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
        private List<Unity3MXTileNode> mReadyUnloadNodes = new();

        // Start is called before the first frame update
        void Start()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (runOnStart)
                Run();
        }

        // Update is called once per frame
        void Update()
        {

        }

        void LateUpdate()
        {
            //������ʱ���Ƿ����updateIntervalTime�������������ִ��
            float nowTime = Time.time;
            if (nowTime - mLastTime < updateIntervalTime)
                return;
            mLastTime = nowTime;
            //�������Ϊ��
            if (mainCamera != null)
            {
                //����CameraState��ִ��Update
                if (mCameraState == null)
                    mCameraState = new CameraState();
                mCameraState.Update(mainCamera);
            }
            //δ��ʼ����δ��ȡ���������������ִ��
            if (!mReady || mCameraState == null || mRootNodes.Count == 0)
                return;
            //����offset������ƫ��
            if (mHasOffset)
                mRootObject.transform.localPosition = mOffset;

            //�����������������
            mRootNodes.Sort(compareRootNode);
            //ִ��node.Update
            foreach (var rootNode in mRootNodes)
            {
                rootNode.Update(mCameraState);
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
                //����readyUnloadNodes
                foreach (var node in mReadyUnloadNodes)
                {
                    //ִ��node.Unload
                    node.Unload();
                }
                //ִ��������readyUnloadNodes
                mReadyUnloadNodes.Clear();
            }
        }

        private int compareRootNode(Unity3MXRootNode left, Unity3MXRootNode right) {
            var result = left.cameraDistance - right.cameraDistance;
            if (result > 0)
                return Mathf.CeilToInt(result);
            else if (result < 0)
                return Mathf.FloorToInt(result);
            return 0;
        }

        public void Run()
        {
            //�Ѽ�����ɣ���Ҫ�ȵ���Clear()������ٴμ���
            if (mReady)
                return;
            //���ڼ����У������ظ�����
            if (mLoading)
                return;
            //���url�Ƿ����
            if (UrlUtils.CheckUrl(url))
            {
                //��ȡbaseUrl
                mBaseUrl = UrlUtils.GetBaseUrl(url);
                Debug.Log("Get baseUrl: " + mBaseUrl);
                //��ʼ��ʼ��
                StartCoroutine(initialize());
            }
        }

        //��ʼ��
        private IEnumerator initialize()
        {
            mLoading = true;
            //��ȡurlָ����ı�
            yield return RequestUtils.GetText(url, null, (string text) =>
            {
                Debug.Log("Get rootJson: " + text);

                parseRootJson(text);
            });

            yield return createRootObject();
            yield return loadRootData();
        }

        //����Json�ı�
        private void parseRootJson(string jsonText)
        {
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
            mBaseDataUrl = UrlUtils.GetBaseUrl(mRootDataPath);

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

        //����������
        private IEnumerator createRootObject()
        {
            mRootObject = new GameObject("Root");
            mRootObject.transform.SetParent(this.transform, false);
            yield return null;
        }

        //���ظ�����
        private IEnumerator loadRootData()
        {
            Unity3MXBLoader loader = new Unity3MXBLoader(this, mRootDataPath);
            loader.onLoad += (HeaderInfo header) =>
            {
                //���ɸ��ڵ�
                for (int i = 0; i < header.nodes.Count; i++)
                {
                    NodeInfo node = header.nodes[i];
                    mRootNodes.Add(new Unity3MXRootNode(this, node));
                }
                //��ʼ�����
                mLoading = false;
                mReady = true;
            };
            yield return loader.Load();
        }

        public void Clear()
        {
            //����δ��ɣ����ܵ���Clear()
            if (!mReady)
                return;
            foreach (var node in mRootNodes)
            {
                node.Destroy();
            }
            mRootNodes.Clear();
            mReadyUnloadNodes.Clear();

            mReady = false;
        }

        internal void AddReadyUnloadNode(Unity3MXTileNode node)
        {
            mReadyUnloadNodes.Add(node);
        }

        //���Node�ĸ�Node�Ƿ���ready״̬���������readyUnloadNodes�б����Ƴ�
        internal void CheckAndRemoveParentNode(Unity3MXTileNode node)
        {
            var parentNode = node.tile.parentNode;
            if (parentNode != null)
            {
                //�ж�parentNode�Ƿ���ready�������readyUnloadNodes�б����Ƴ�
                if (parentNode.isReady)
                    mReadyUnloadNodes.Remove(parentNode);
                //����������ϵݹ����
                else
                    CheckAndRemoveParentNode(parentNode);
            }
        }
    }

    public class CameraState
    {
        public Camera camera;
        public Plane[] planes;
        public float fieldOfView;
        public float nearClipPlane;
        public float inverseNear;
        public float topClipPlane;
        public float rightClipPlane;

        public void Update(Camera camera)
        {
            bool changed = false;
            if (this.camera != camera)
            {
                this.camera = camera;
                changed = true;
            }
            //��ȡ�����׶��planes
            this.planes = GeometryUtility.CalculateFrustumPlanes(camera);
            if (this.fieldOfView != camera.fieldOfView)
            {
                this.fieldOfView = camera.fieldOfView;
                changed = true;
            }
            if (this.nearClipPlane != camera.nearClipPlane)
            {
                this.nearClipPlane = camera.nearClipPlane;
                //����1.0��nearClipPlane��ֵ
                this.inverseNear = 1.0f / this.nearClipPlane;
                changed = true;
            }
            if (changed)
            {
                //����topClipPlane
                this.topClipPlane = this.nearClipPlane * Mathf.Tan(0.5f * this.fieldOfView * Mathf.Deg2Rad);
                //����rightClipPlane
                this.rightClipPlane = camera.aspect * this.topClipPlane;
            }
        }

        //�����Ƿ�ɼ�
        public bool TestVisibile(Bounds bounds)
        {
            return GeometryUtility.TestPlanesAABB(this.planes, bounds);
        }

        //���㵥λ���ش�С������/����
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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity3MX
{
    public class Unity3MXRootNode
    {
        private Unity3MXComponent mRootComponent;
        private NodeInfo mNodeInfo;
        public NodeInfo nodeInfo
        {
            get { return mNodeInfo; }
        }
        public string id
        {
            get { return mNodeInfo.id; }
        }
        private string mBaseUrl;
        public string baseUrl
        {
            get { return mBaseUrl; }
        }
        private List<Unity3MXTile> mChildTiles = new();
        private GameObject mGameObject;
        public GameObject gameObject
        {
            get { return mGameObject; }
        }
        public bool isReady
        {
            get { return mGameObject; }
        }
        private BoxCollider mCollider;
        private float mCameraDistance;
        public float cameraDistance
        {
            get { return mCameraDistance; }
        }
        private bool mDestroyed = false;
        public bool isDestroyed
        {
            get { return mDestroyed; }
        }

        public Unity3MXRootNode(Unity3MXComponent rootComponent, NodeInfo nodeInfo)
        {
            mRootComponent = rootComponent;
            mNodeInfo = nodeInfo;
            //��ȡbaseUrl
            mBaseUrl = rootComponent.baseDataUrl + nodeInfo.id + "/";
        }

        public void Update(CameraState cameraState)
        {
            if (mDestroyed)
                return;
            //gameObjectΪnull��˵����Node��δ��ʼ��
            if (mGameObject == null)
            {
                //����gameObject
                mGameObject = new GameObject(mNodeInfo.id);
                //����BoxCollider�����ڲ����Ƿ�ɼ�
                mCollider = mGameObject.AddComponent<BoxCollider>();
                mCollider.center = mNodeInfo.bounds.center;
                mCollider.size = mNodeInfo.bounds.size;
                //���ص�rootObject��
                mGameObject.transform.SetParent(mRootComponent.rootObject.transform, false);
                //���㵱ǰNode���ĵ㵽���λ�õľ���
                mCameraDistance = Vector3.Distance(mCollider.bounds.center, cameraState.camera.transform.position);
                //��ɺ�ֱ��return���ȴ���һ֡��RootNode�������ִ������Ĵ���
                return;
            }
            //���㵱ǰNode���ĵ㵽���λ�õľ���
            mCameraDistance = Vector3.Distance(mCollider.bounds.center, cameraState.camera.transform.position);
            //�ж�Node�Ƿ�ɼ�
            if (cameraState.TestVisibile(mCollider.bounds))
            {
                //Node�ɼ�������ensureChildTiles
                ensureChildTiles();
                //ִ��tile.Update
                foreach (var tile in mChildTiles)
                {
                    tile.Update(cameraState);
                }
            }
            else
            {
                //Node���ɼ���enableMemeoryCacheΪtrueʱֻж�ز�����
                if (mRootComponent.enableMemeoryCache)
                {
                    UnloadChildren();
                }
                //Ϊfalseʱ����������Tile
                else
                {
                    DestroyChildren();
                }
            }
        }

        //�ж���Tile�Ƿ��Ѵ�����δ�����򴴽�
        private void ensureChildTiles()
        {
            if (mChildTiles.Count == 0)
            {
                for (int i = 0; i < mNodeInfo.children.Count; i++)
                {
                    mChildTiles.Add(new Unity3MXTile(mRootComponent, this, mNodeInfo.children[i]));
                }
            }
        }

        //����gameObject
        private void destroyGameObject()
        {
            if (mGameObject != null)
            {
                Object.Destroy(mGameObject);
                mGameObject = null;
            }
        }

        //ж��������Tile
        public void UnloadChildren()
        {
            foreach (var tile in mChildTiles)
            {
                tile.Unload(true);
            }
        }

        //����������Tile
        public void DestroyChildren()
        {
            foreach (var tile in mChildTiles)
            {
                tile.Destroy();
            }
            mChildTiles.Clear();
        }

        //����
        public void Destroy()
        {
            if (mDestroyed)
                return;

            DestroyChildren();

            destroyGameObject();

            mDestroyed = true;
        }
    }
}

﻿using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEditor;
using System.Reflection;
using System;
using System.Linq;
using UnityEditorInternal;

using Status = UnityEngine.UIElements.DropdownMenuAction.Status;
using NodeView = UnityEditor.Experimental.GraphView.Node;

namespace GraphProcessor
{
    [NodeCustomEditor(typeof(BaseNode))]
    public class BaseNodeView : NodeView
    {
        public BaseNode nodeTarget;

        public List<PortView> inputPortViews = new List<PortView>();
        public List<PortView> outputPortViews = new List<PortView>();

        public BaseGraphView owner { private set; get; }

        protected Dictionary<string, List<PortView>> portsPerFieldName = new Dictionary<string, List<PortView>>();

        protected VisualElement controlsContainer;
        protected VisualElement debugContainer;

        Label computeOrderLabel = new Label();

        public event Action<PortView> onPortConnected;
        public event Action<PortView> onPortDisconnected;

        public bool initializing = false; //Used for applying SetPosition on locked node at init.

        readonly string baseNodeStyle = "GraphProcessorStyles/BaseNodeView";

        #region  Initialization

        public void Initialize(BaseGraphView owner, BaseNode node)
        {
            nodeTarget = node;
            this.owner = owner;

            owner.computeOrderUpdated += ComputeOrderUpdatedCallback;

            styleSheets.Add(Resources.Load<StyleSheet>(baseNodeStyle));

            if (!string.IsNullOrEmpty(node.layoutStyle))
                styleSheets.Add(Resources.Load<StyleSheet>(node.layoutStyle));

            InitializePorts();
            InitializeView();
            InitializeDebug();

            Enable();

            this.RefreshPorts();
        }

        void InitializePorts()
        {
            var listener = owner.connectorListener;

            foreach (var inputPort in nodeTarget.inputPorts)
            {
                AddPort(inputPort.fieldInfo, Direction.Input, listener, inputPort.portData);
            }

            foreach (var outputPort in nodeTarget.outputPorts)
            {
                AddPort(outputPort.fieldInfo, Direction.Output, listener, outputPort.portData);
            }
        }

        void InitializeView()
        {
            controlsContainer = new VisualElement { name = "controls" };
            mainContainer.Add(controlsContainer);

            debugContainer = new VisualElement { name = "debug" };
            if (nodeTarget.debug)
                mainContainer.Add(debugContainer);

            title = (string.IsNullOrEmpty(nodeTarget.name)) ? nodeTarget.GetType().Name : nodeTarget.name;

            initializing = true;

            SetPosition(nodeTarget.position);
        }

        void InitializeDebug()
        {
            ComputeOrderUpdatedCallback();
            debugContainer.Add(computeOrderLabel);
        }

        #endregion

        #region API

        public List<PortView> GetPortViewsFromFieldName(string fieldName)
        {
            List<PortView> ret;

            portsPerFieldName.TryGetValue(fieldName, out ret);

            return ret;
        }

        public PortView GetFirstPortViewFromFieldName(string fieldName)
        {
            return GetPortViewsFromFieldName(fieldName)?.First();
        }

        public PortView GetPortViewFromFieldName(string fieldName, string identifier)
        {
            return GetPortViewsFromFieldName(fieldName)?.FirstOrDefault(pv =>
            {
                return (pv.portData.identifier == identifier) || (String.IsNullOrEmpty(pv.portData.identifier) && String.IsNullOrEmpty(identifier));
            });
        }

        public PortView AddPort(FieldInfo fieldInfo, Direction direction, EdgeConnectorListener listener, PortData portData)
        {
            // TODO: hardcoded value
            PortView p = new PortView(Orientation.Horizontal, direction, fieldInfo, portData, listener);

            if (p.direction == Direction.Input)
            {
                inputPortViews.Add(p);
                inputContainer.Add(p);
            }
            else
            {
                outputPortViews.Add(p);
                outputContainer.Add(p);
            }

            p.Initialize(this, portData?.displayName);

            List<PortView> ports;
            portsPerFieldName.TryGetValue(p.fieldName, out ports);
            if (ports == null)
            {
                ports = new List<PortView>();
                portsPerFieldName[p.fieldName] = ports;
            }
            ports.Add(p);

            return p;
        }

        public void RemovePort(PortView p)
        {
            // Remove all connected edges:
            var edgesCopy = p.GetEdges().ToList();
            foreach (var e in edgesCopy)
                owner.Disconnect(e, refreshPorts: false);

            if (p.direction == Direction.Input)
            {
                inputPortViews.Remove(p);
                inputContainer.Remove(p);
            }
            else
            {
                outputPortViews.Remove(p);
                outputContainer.Remove(p);
            }

            List<PortView> ports;
            portsPerFieldName.TryGetValue(p.fieldName, out ports);
            ports.Remove(p);
        }

        public void OpenNodeViewScript()
        {
            var scriptPath = NodeProvider.GetNodeViewScript(GetType());

            if (scriptPath != null)
                InternalEditorUtility.OpenFileAtLineExternal(scriptPath, 0);
        }

        public void OpenNodeScript()
        {
            var scriptPath = NodeProvider.GetNodeScript(nodeTarget.GetType());

            if (scriptPath != null)
                InternalEditorUtility.OpenFileAtLineExternal(scriptPath, 0);
        }

        public void ToggleDebug()
        {
            nodeTarget.debug = !nodeTarget.debug;
            UpdateDebugView();
        }

        public void UpdateDebugView()
        {
            if (nodeTarget.debug)
                mainContainer.Add(debugContainer);
            else
                mainContainer.Remove(debugContainer);
        }

        #endregion

        #region Callbacks & Overrides

        void ComputeOrderUpdatedCallback()
        {
            //Update debug compute order
            computeOrderLabel.text = "Compute order: " + nodeTarget.computeOrder;
        }

        public virtual void Enable()
        {
            DrawDefaultInspector();
        }

        public virtual void DrawDefaultInspector()
        {
            var fields = nodeTarget.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var field in fields)
            {
                //skip if the field is not serializable
                if (!field.IsPublic && field.GetCustomAttribute(typeof(SerializeField)) == null)
                    continue;

                //skip if the field is an input/output and not marked as SerializedField
                if (field.GetCustomAttribute(typeof(SerializeField)) == null && (field.GetCustomAttribute(typeof(InputAttribute)) != null || field.GetCustomAttribute(typeof(OutputAttribute)) != null))
                    continue;

                //skip if marked with NonSerialized or HideInInspector
                if (field.GetCustomAttribute(typeof(System.NonSerializedAttribute)) != null || field.GetCustomAttribute(typeof(HideInInspector)) != null)
                    continue;

                var controlLabel = new Label(field.Name);
                controlsContainer.Add(controlLabel);

                var element = FieldFactory.CreateField(field.FieldType, field.GetValue(nodeTarget), (newValue) =>
                {
                    owner.RegisterCompleteObjectUndo("Updated " + newValue);
                    field.SetValue(nodeTarget, newValue);
                });

                if (element != null)
                    controlsContainer.Add(element);
            }
        }

        public void OnPortConnected(PortView port)
        {
            onPortConnected?.Invoke(port);
        }

        public void OnPortDisconnected(PortView port)
        {
            onPortDisconnected?.Invoke(port);
        }

        // TODO: a function to force to reload the custom behavior ports (if we want to do a button to add ports for example)

        public virtual void OnRemoved() { }
        public virtual void OnCreated() { }

        public override void SetPosition(Rect newPos)
        {
            if (initializing || !nodeTarget.isLocked)
            {
                initializing = false;
                base.SetPosition(newPos);

                Undo.RegisterCompleteObjectUndo(owner.graph, "Moved graph node");
                nodeTarget.position = newPos;
            }
        }

        public override bool expanded
        {
            get { return base.expanded; }
            set
            {
                base.expanded = value;
                nodeTarget.expanded = value;
            }
        }

        public void ChangeLockStatus()
        {
            nodeTarget.nodeLock ^= true;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Open Node Script", (e) => OpenNodeScript(), OpenNodeScriptStatus);
            evt.menu.AppendAction("Open Node View Script", (e) => OpenNodeViewScript(), OpenNodeViewScriptStatus);
            evt.menu.AppendAction("Debug", (e) => ToggleDebug(), DebugStatus);
            if (nodeTarget.unlockable)
                evt.menu.AppendAction((nodeTarget.isLocked ? "Unlock" : "Lock"), (e) => ChangeLockStatus(), LockStatus);
        }

        Status LockStatus(DropdownMenuAction action)
        {
            return Status.Normal;
        }

        Status DebugStatus(DropdownMenuAction action)
        {
            if (nodeTarget.debug)
                return Status.Checked;
            return Status.Normal;
        }

        Status OpenNodeScriptStatus(DropdownMenuAction action)
        {
            if (NodeProvider.GetNodeScript(nodeTarget.GetType()) != null)
                return Status.Normal;
            return Status.Disabled;
        }

        Status OpenNodeViewScriptStatus(DropdownMenuAction action)
        {
            if (NodeProvider.GetNodeViewScript(GetType()) != null)
                return Status.Normal;
            return Status.Disabled;
        }

        void SyncPortCounts(IEnumerable<NodePort> ports, IEnumerable<PortView> portViews)
        {
            var listener = owner.connectorListener;

            // Maybe not good to remove ports as edges are still connected :/
            foreach (var pv in portViews.ToList())
            {
                // If the port have disepeared from the node datas, we remove the view:
                // We can use the identifier here because this function will only be called when there is a custom port behavior
                if (!ports.Any(p => p.portData.identifier == pv.portData.identifier))
                    RemovePort(pv);
            }

            foreach (var p in ports)
            {
                // Add missing port views
                if (!portViews.Any(pv => p.portData.identifier == pv.portData.identifier))
                    AddPort(p.fieldInfo, Direction.Input, listener, p.portData);
            }
        }

        public new bool RefreshPorts()
        {
            // If a port behavior was attached to one port, then
            // the port count might have been updated by the node
            // so we have to refresh the list of port views.
            UpdatePortViewWithPorts(nodeTarget.inputPorts, inputPortViews);
            UpdatePortViewWithPorts(nodeTarget.outputPorts, outputPortViews);

            void UpdatePortViewWithPorts(NodePortContainer ports, List<PortView> portViews)
            {
                // When there is no current portviews, we can't zip the list so we just add all
                if (portViews.Count == 0)
                    SyncPortCounts(ports, new PortView[] { });
                else if (ports.Count == 0) // Same when there is no ports
                    SyncPortCounts(new NodePort[] { }, portViews);
                else
                {
                    var p = ports.GroupBy(n => n.fieldName);
                    var pv = portViews.GroupBy(v => v.fieldName);
                    p.Zip(pv, (portPerFieldName, portViewPerFieldName) =>
                    {
                        if (portPerFieldName.Count() != portViewPerFieldName.Count())
                            SyncPortCounts(portPerFieldName, portViewPerFieldName);
                        // We don't care about the result, we just iterate over port and portView
                        return "";
                    }).ToList();
                }

                // Here we're sure that we have the same amout of port and portView
                // so we can update the view with the new port datas (if the name of a port have been changed for example)

                for (int i = 0; i < portViews.Count; i++)
                {
                    var pv = portViews[i];

                    pv.UpdatePortView(ports[i].portData.displayName, ports[i].portData.displayType);
                }
            }

            return base.RefreshPorts();
        }

        protected void ForceUpdatePorts()
        {
            nodeTarget.UpdateAllPorts();

            RefreshPorts();
        }

        #endregion
    }
}
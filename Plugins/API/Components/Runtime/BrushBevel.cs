using System.Collections.Generic;
using RealtimeCSG;
using RealtimeCSG.Components;
using RealtimeCSG.Legacy;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Rogue.LevelDesign
{
	[ExecuteInEditMode]
    public class BrushBevel : MonoBehaviour
    {
	    private static int bevelRange = 1;
	    private static HashSet<BrushBevel> instances = new();
	    [ReadOnly] public bool Active = false;
	    
	    public float Distance = 0.1f;
	    [Range(0, 5)] public int Iterations = 1;
	    public bool Smooth = false;

        [Required] public CSGBrush Brush = null!;

        public ControlMesh? ControlMesh;
        public Shape? Shape;
        public Shape? BeveledShape;

        private void OnEnable()
        {
	        instances.Add(this);

	        if (EditModeManager.EditMode is ToolEditMode.Place or ToolEditMode.Surfaces && Brush)
		        Bevel();
        }

        private void OnDisable()
        {
	        instances.Remove(this);

	        Rollback();
        }

        [InitializeOnLoadMethod]
        static void RegisterProjectValidationRules()
        {
	        SceneView.duringSceneGui += SceneViewDuringSceneGui;
        }

        private static void SceneViewDuringSceneGui(SceneView obj)
        {
	        bool shouldBeActive = EditModeManager.EditMode is ToolEditMode.Surfaces || EditModeManager.EditMode is ToolEditMode.Place && Tools.current == Tool.Move;
	        foreach (var brushBevel in instances)
	        {
		        bool shouldBeActiveForThisBrush = shouldBeActive || Selection.Contains(brushBevel.gameObject) == false;
		        if (brushBevel.Active != shouldBeActiveForThisBrush && brushBevel.enabled)
		        {
			        if (shouldBeActiveForThisBrush)
				        brushBevel.StoreAndBevel();
			        else
				        brushBevel.Rollback();
		        }
	        }

	        if (Selection.activeObject is GameObject go)
	        {
		        if (shouldBeActive && go.GetComponent<BrushBevel>() is {} bb)
		        {
			        Handles.BeginGUI();
			        EditorGUI.BeginChangeCheck();
			        GUILayout.BeginArea(new Rect(0, 256, 256, EditorGUIUtility.singleLineHeight*4));
			        var prevLabelWidth = EditorGUIUtility.labelWidth;
			        EditorGUIUtility.labelWidth = 100;

			        GUILayout.BeginHorizontal();
			        {
				        bb.Distance = EditorGUILayout.Slider("Bevel", bb.Distance, 0f, bevelRange / 4f);
				        if (GUILayout.Button(new GUIContent("+"), EditorStyles.miniButtonLeft))
					        bevelRange++;
				        if (GUILayout.Button(new GUIContent("-"), EditorStyles.miniButtonLeft))
					        bevelRange--;
				        if (GUILayout.Button(new GUIContent("x"), EditorStyles.miniButtonLeft))
					        foreach (var o in Selection.objects)
						        if (o is GameObject go3 && go3.GetComponent<CSGBrush>() && go3.GetComponent<BrushBevel>() is {} b)
							        DestroyImmediate(b);
			        }
			        GUILayout.EndHorizontal();
			        bb.Iterations = EditorGUILayout.IntSlider("Iterations", bb.Iterations, 0, 5);
			        bb.Smooth = EditorGUILayout.Toggle("Smooth", bb.Smooth);

			        if (EditorGUI.EndChangeCheck())
			        {
				        foreach (var o in Selection.objects)
				        {
					        if (o is GameObject go2 && go2.GetComponent<BrushBevel>() is { } bb2)
					        {
						        bb2.Distance = bb.Distance;
						        bb2.Iterations = bb.Iterations;
						        bb2.Active = false;
						        bb2.Bevel();
					        }
				        }
			        }

			        EditorGUIUtility.labelWidth = prevLabelWidth;
			        GUILayout.EndArea();
			        Handles.EndGUI();
		        }
		        else if (go.GetComponent<CSGBrush>() && go.GetComponent<BrushBevel>() == null)
		        {
			        Handles.BeginGUI();

			        if (GUI.Button(new Rect(0, 0, 128, EditorGUIUtility.singleLineHeight), new GUIContent("Bevel")))
			        {
				        foreach (var o in Selection.objects)
				        {
					        if (o is GameObject selectedGo && selectedGo.GetComponent<CSGBrush>() is {} brush && selectedGo.GetComponent<BrushBevel>() == null)
					        {
						        var bevel = selectedGo.AddComponent<BrushBevel>();
						        bevel.Brush = brush;
						        bevel.StoreAndBevel();
					        }
				        }
			        }

			        Handles.EndGUI();
		        }
	        }
        }

        private void StoreAndBevel()
        {
	        if (Active)
		        return;

	        ControlMesh = Brush.ControlMesh.Clone();
	        Shape = Brush.Shape.Clone();
	        Bevel();
        }

        private void Rollback()
        {
	        if (Active)
		        BeveledShape = Brush.Shape.Clone();

	        Active = false;
	        if (ControlMesh?.Edges == null || ControlMesh.Edges.Length == 0)
		        ControlMesh = Brush.ControlMesh.Clone();
	        if (Shape?.Surfaces == null || Shape.Surfaces.Length == 0)
		        Shape = Brush.Shape.Clone();
	        
	        Brush.ControlMesh = ControlMesh.Clone();
	        Brush.Shape = Shape.Clone();
	        Brush.ControlMesh.SetDirty();
	        ControlMeshUtility.RebuildShape(Brush);
	        InternalCSGModelManager.CheckSurfaceModifications(Brush, true);
	        InternalCSGModelManager.CheckForChanges(true);
        }

        private void Bevel()
        {
	        if (Active)
		        return;

	        Active = true;
	        if (ControlMesh?.Edges == null || ControlMesh.Edges.Length == 0)
		        ControlMesh = Brush.ControlMesh.Clone();
	        if (Shape?.Surfaces == null || Shape.Surfaces.Length == 0)
		        Shape = Brush.Shape.Clone();

            var brushModified = false;
            var brush		= Brush;
            var controlMesh = ControlMesh.Clone();
            var shape		= Shape.Clone();
            var transform	= brush.transform;

            for (int _ = 0; _ < Iterations; _++)
            {
	            var halfEdgeIndices = new List<int>();
	            if (Distance > 0)
	            {
		            for (int i = 0; i < controlMesh.Edges.Length; i++)
		            {
			            if (controlMesh.Edges[i].TwinIndex > i)
				            halfEdgeIndices.Add(i);
		            }
	            }

	            var selected_edges		= new List<int>();
	            var planes				= new HashSet<CSGPlane>();
	            //if (chamferMode == ChamferMode.Edge)
	            {
	                for (int e = 0; e < halfEdgeIndices.Count; e++)
	                {
	                    var halfEdgeIndex = halfEdgeIndices[e];
	                    var twinIndex = controlMesh.Edges[halfEdgeIndex].TwinIndex;
	                    var polygonIndex1 = controlMesh.Edges[halfEdgeIndex].PolygonIndex;
	                    var polygonIndex2 = controlMesh.Edges[twinIndex].PolygonIndex;

	                    var pointIndex1 = controlMesh.Edges[halfEdgeIndex];
	                    var pointIndex2 = controlMesh.Edges[twinIndex];

	                    var texGenIndex1 = controlMesh.Polygons[polygonIndex1].TexGenIndex;
	                    var texGenIndex2 = controlMesh.Polygons[polygonIndex2].TexGenIndex;
	                    var chamferPlanes0 = shape.Surfaces[texGenIndex1].Plane;
	                    var chamferPlanes1 = shape.Surfaces[texGenIndex2].Plane;

	                    var localToWorldMatrix = transform.localToWorldMatrix;
	                    chamferPlanes0.Transform(localToWorldMatrix);
	                    chamferPlanes1.Transform(localToWorldMatrix);

	                    var chamferEdgePoints0 = localToWorldMatrix.MultiplyPoint(controlMesh.Vertices[pointIndex1.VertexIndex]);//meshState.WorldPoints[pointIndex1];
	                    var chamferEdgePoints1 = localToWorldMatrix.MultiplyPoint(controlMesh.Vertices[pointIndex2.VertexIndex]);//meshState.WorldPoints[pointIndex2];

	                    var chamferEdge = (chamferEdgePoints1 - chamferEdgePoints0).normalized;

	                    var chamferTangents0 = Vector3.Cross(chamferPlanes0.normal, chamferEdge);
	                    var chamferTangents1 = Vector3.Cross(chamferPlanes1.normal, chamferEdge);

	                    if (chamferPlanes1.Distance(chamferEdgePoints0 + chamferTangents0) > 0)
	                        chamferTangents0 = -chamferTangents0;
	                    if (chamferPlanes0.Distance(chamferEdgePoints0 + chamferTangents1) > 0)
	                        chamferTangents1 = -chamferTangents1;

	                    var delta = chamferTangents0 * Distance;
	                    var chamferPoints0 = chamferEdgePoints0 + delta;
	                    var chamferPoints1 = chamferEdgePoints1 + delta;

	                    delta = chamferTangents1 * Distance;
	                    var chamferPoints2 = chamferEdgePoints0 + delta;
	                    //var chamferPoints3 = chamferEdgePoints1 + delta;

	                    //render_lines.Add(chamferPoints0);
	                    //render_lines.Add(chamferPoints1);
	                    //render_lines.Add(chamferPoints2);
	                    //render_lines.Add(chamferPoints3);

	                    var chamferPlane = new CSGPlane(chamferPoints0, chamferPoints1, chamferPoints2);

	                    var localCuttingPlane = InverseTransformPlane(brush.transform.localToWorldMatrix, chamferPlane);

	                    planes.Add(localCuttingPlane);

	                    static CSGPlane InverseTransformPlane(Matrix4x4 inverseMatrix, CSGPlane plane)
	                    {
	                        var dstMatrix = Matrix4x4.Transpose(inverseMatrix);
	                        var dstPlaneV = dstMatrix * new Vector4(plane.a, plane.b, plane.c, -plane.d);
	                        return new CSGPlane(dstPlaneV.x, dstPlaneV.y, dstPlaneV.z, -dstPlaneV.w);
	                    }
	                }
	            }

	            foreach(var plane in planes)
	            {
	                selected_edges.Clear();
	                if (ControlMeshUtility.CutMesh(controlMesh, shape, plane, ref selected_edges))
	                {
	                    var edge_loop = ControlMeshUtility.FindEdgeLoop(controlMesh, ref selected_edges);
	                    if (edge_loop != null)
	                    {
	                        if (ControlMeshUtility.SplitEdgeLoop(controlMesh, shape, edge_loop))
	                        {
	                            Shape foundShape;
	                            ControlMesh foundControlMesh;
	                            if (ControlMeshUtility.FindAndDetachSeparatePiece(controlMesh, shape, plane, out foundControlMesh, out foundShape))
	                            {
	                                controlMesh = foundControlMesh;
	                                shape = foundShape;
	                                brushModified = true;
	                            }
	                        }
	                    }
	                }
	            }
            }

            if (brushModified)
			{
				if (Smooth)
				{
					for (int i = 0; i < shape.TexGens.Length; i++)
						shape.TexGens[i].SmoothingGroup = 1;
				}

				if (BeveledShape?.Surfaces == null || BeveledShape.Surfaces.Length == 0)
				{
					BeveledShape = shape.Clone();
				}
				else
				{
					// Apply the custom data over shape
					for (int i = 0; i < BeveledShape.Surfaces.Length; i++)
						shape.Surfaces[i].TexGenIndex = BeveledShape.Surfaces[i].TexGenIndex;
					for (int i = 0; i < BeveledShape.TexGenFlags.Length; i++)
						shape.TexGenFlags[i] = BeveledShape.TexGenFlags[i];
					for (int i = 0; i < BeveledShape.TexGens.Length; i++)
						shape.TexGens[i] = BeveledShape.TexGens[i];
				}

				brush.ControlMesh = controlMesh;
				brush.Shape = shape;
				brush.ControlMesh.SetDirty();
				ControlMeshUtility.RebuildShape(brush);
				if (brush.ChildData?.Model != null)
					InternalCSGModelManager.CheckSurfaceModifications(brush, true);
				
				InternalCSGModelManager.CheckForChanges(true);
			}
        }
    }
}
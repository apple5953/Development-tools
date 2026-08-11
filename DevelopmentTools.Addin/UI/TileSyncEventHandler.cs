using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;
using DevelopmentTools.Algorithms;
using DevelopmentTools.Commands;
using DevelopmentTools.Core;
using DevelopmentTools.Generators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
namespace DevelopmentTools.UI
{
	public class RoomSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is Room;
		}
		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public class WallSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is Wall;
		}
		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public class TileSyncEventHandler : IExternalEventHandler
	{
		public enum Operation
		{
			PickFaceAndPat,
			ConfirmMaterialPat,
			Generate3DTiles,
			Generate3DFloorTiles,
			ChangeLocalTileMaterial,
			CalculatePlaneQuantity,
			Calculate3DQuantity,
			CreateCurrentSpaceTileSchedule,
			HighlightFullTileParts,
			HighlightHalfTileParts,
			HighlightUnderHalfTileParts,
			AddJointParamToWalls,
			AddJointParamToFloors,
			DeleteTiles,
			CreateSchedule,
			ExportExcel,
			ConvertToEditable,
			ConfirmCalibrationAndCreateParts,
			DeleteTileParts,
			Step1CreateRawParts,
			SaveTileLayoutFaces,
			Step2CalculateSplitCurves,
			Step3ExecuteDivideParts,
			EditPartsSketch,
			OpenWallJoinTool,
			OpenTilePurchaseDashboard,
			None
		}

		private enum TileBoundaryJoinMode
		{
			MiterJoin = 0,
			IndependentToBoundary = 1,
			OuterCornerWrap = 2,
			InnerCornerGroutBreak = 3
		}
		private class ExportTileData
		{
			public string ElementId
			{
				get;
				set;
			}
			public string Tile_ID
			{
				get;
				set;
			}
			public string Anchor_ID
			{
				get;
				set;
			}
			public string Room_ID
			{
				get;
				set;
			}
			public string Surface_ID
			{
				get;
				set;
			}
			public string Tile_Type
			{
				get;
				set;
			}
			public string Host_ID
			{
				get;
				set;
			}
			public double Area
			{
				get;
				set;
			}
			public double Width
			{
				get;
				set;
			}
			public double Height
			{
				get;
				set;
			}
			public double Thickness
			{
				get;
				set;
			}
		}
		[CompilerGenerated]
		[Serializable]
		private sealed class __c
		{
			public static readonly TileSyncEventHandler.__c __9 = new TileSyncEventHandler.__c();
			public static Func<XYZ, double> __9__22_0;
			public static Func<XYZ, double> __9__22_1;
			public static Func<XYZ, double> __9__22_2;
			internal double __DetectRoomIdOfTile_b__22_0(XYZ p)
			{
				return p.X;
			}
			internal double __DetectRoomIdOfTile_b__22_1(XYZ p)
			{
				return p.Y;
			}
			internal double __DetectRoomIdOfTile_b__22_2(XYZ p)
			{
				return p.Z;
			}
		}
		private readonly List<ElementId> _lastCreatedTileIds;
		private readonly List<ElementId> createdTileIds;
		private string _currentTileLayoutSelectionLabel = string.Empty;
		private readonly List<ElementId> _currentTileLayoutSelectionPartIds = new List<ElementId>();
		public TileSyncEventHandler.Operation CurrentOperation
		{
			get;
			set;
		}
		public MainViewModel ViewModel
		{
			get;
			set;
		}
		public void Execute(UIApplication app)
		{
			bool flag = this.ViewModel == null;
			if (!flag)
			{
				UIDocument activeUIDocument = app.ActiveUIDocument;
				Document document = activeUIDocument.Document;
				switch (this.CurrentOperation)
				{
				case TileSyncEventHandler.Operation.PickFaceAndPat:
					this.DoPickFaceAndPat(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.ConfirmMaterialPat:
					this.DoConfirmMaterialPat(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.Generate3DTiles:
					this.DoGenerate3DTiles(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.Generate3DFloorTiles:
					this.DoGenerate3DFloorTiles(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.ChangeLocalTileMaterial:
					this.DoChangeLocalTileMaterial(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.CalculatePlaneQuantity:
					this.DoCalculatePlaneQuantity(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.Calculate3DQuantity:
					this.DoCalculate3DQuantity(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.CreateCurrentSpaceTileSchedule:
					this.DoCreateCurrentSpaceTileSchedule(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.HighlightFullTileParts:
					this.DoHighlightTilePartsByType(activeUIDocument, document, "Full", "整磚");
					break;
				case TileSyncEventHandler.Operation.HighlightHalfTileParts:
					this.DoHighlightTilePartsByType(activeUIDocument, document, "HalfCut", "半磚");
					break;
				case TileSyncEventHandler.Operation.HighlightUnderHalfTileParts:
					this.DoHighlightTilePartsByType(activeUIDocument, document, "UnderHalfCut", "不足半磚");
					break;
				case TileSyncEventHandler.Operation.AddJointParamToWalls:
					this.DoAddJointParam(app.Application, document, true);
					break;
				case TileSyncEventHandler.Operation.AddJointParamToFloors:
					this.DoAddJointParam(app.Application, document, false);
					break;
				case TileSyncEventHandler.Operation.DeleteTiles:
					this.DoDeleteTiles(document);
					break;
				case TileSyncEventHandler.Operation.CreateSchedule:
					this.DoEstimateSelectedWallFaces(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.ExportExcel:
					this.DoExportExcel(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.ConvertToEditable:
					this.DoConvertToEditable(app, activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.ConfirmCalibrationAndCreateParts:
					this.DoConfirmCalibrationAndCreateParts(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.DeleteTileParts:
					this.DoDeleteTileParts(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.Step1CreateRawParts:
					this.DoStep1CreateRawParts(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.SaveTileLayoutFaces:
					this.DoSaveTileLayoutFaces(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.Step2CalculateSplitCurves:
					this.DoStep2CalculateSplitCurves(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.Step3ExecuteDivideParts:
					this.DoStep3ExecuteDivideParts(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.EditPartsSketch:
					this.DoEditPartsSketch(app, activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.OpenWallJoinTool:
					this.DoOpenWallJoinTool(app, activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.OpenTilePurchaseDashboard:
					this.DoOpenTilePurchaseDashboard(activeUIDocument, document);
					break;
				}
				this.CurrentOperation = TileSyncEventHandler.Operation.None;
			}
		}
		public string GetName()
		{
			return "RoomTileSync";
		}
		private void DoPickFaceAndPat(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("請選取已繪製填充線的面。");
				Reference reference = null;
				try
				{
					reference = uidoc.Selection.PickObject(ObjectType.Face, "請選取已繪製填充線的面。");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					this.ViewModel.SetStatus("處理中...");
					return;
				}
				bool flag = reference == null;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					Element element = doc.GetElement(reference.ElementId);
					Face face = ((element != null) ? element.GetGeometryObjectFromReference(reference) : null) as Face;
					bool flag2 = face == null || !(face is PlanarFace);
					if (flag2)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						bool flag3 = element is Floor;
						TilePatternParams tilePatternParams = TileDimensionDetector.DetectPatternParams(element, doc, this.ViewModel.TileWidth, this.ViewModel.TileHeight, this.ViewModel.JointWidth, this.ViewModel.TileThickness);
						this.ViewModel.TileWidth = tilePatternParams.TileWidth;
						this.ViewModel.TileHeight = tilePatternParams.TileHeight;
						this.ViewModel.JointWidth = tilePatternParams.JointWidth;
						this.ViewModel.TileThickness = tilePatternParams.Thickness;
						using (Transaction transaction = new Transaction(doc, "磁磚 PAT 填充樣式"))
						{
							transaction.Start();
							ElementId orCreateTileHatchMaterial = this.GetOrCreateTileHatchMaterial(doc, tilePatternParams);
							bool flag4 = orCreateTileHatchMaterial != ElementId.InvalidElementId;
							if (flag4)
							{
								bool flag5 = doc.IsPainted(element.Id, face);
								if (flag5)
								{
									doc.RemovePaint(element.Id, face);
								}
								doc.Paint(element.Id, face, orCreateTileHatchMaterial);
							}
							transaction.Commit();
						}
						uidoc.RefreshActiveView();
						this.ViewModel.SetStatus(string.Format("磁磚配置系統作業", new object[]
						{
							flag3 ? "磁磚配置系統作業" : "磁磚配置系統作業",
							tilePatternParams.TileWidth,
							tilePatternParams.TileHeight,
							tilePatternParams.JointWidth
						}));
					}
				}
			}
			catch (Exception ex)
			{
				this.ViewModel.SetStatus("磁磚配置系統作業" + ex.Message);
			}
		}
		private void DoAddJointParam(Application app, Document doc, bool isWall)
		{
			string text = isWall ? "牆面" : "地坪";
			this.ViewModel.SetStatus("處理中...");
			string sharedParametersFilename = app.SharedParametersFilename;
			try
			{
				string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				string text2 = Path.Combine(directoryName, "TileJointSharedParam.txt");
				this.EnsureJointSharedParamFile(text2);
				app.SharedParametersFilename = text2;
				DefinitionFile definitionFile = app.OpenSharedParameterFile();
				bool flag = definitionFile == null;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					DefinitionGroup definitionGroup = definitionFile.Groups.get_Item("磁磚系統");
					bool flag2 = definitionGroup == null;
					if (flag2)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						ExternalDefinition externalDefinition = definitionGroup.Definitions.get_Item("Tile_Joint_Width") as ExternalDefinition;
						bool flag3 = externalDefinition == null;
						if (flag3)
						{
							this.ViewModel.SetStatus("處理中...");
						}
						else
						{
							BuiltInCategory categoryId = isWall ? BuiltInCategory.OST_Walls : BuiltInCategory.OST_Floors;
							Category category = doc.Settings.Categories.get_Item(categoryId);
							bool flag4 = category == null;
							if (flag4)
							{
								this.ViewModel.SetStatus("處理中...");
							}
							else
							{
								CategorySet categorySet = app.Create.NewCategorySet();
								categorySet.Insert(category);
								int num = 0;
								using (Transaction transaction = new Transaction(doc, "新增縫隙參數"))
								{
									transaction.Start();
									BindingMap parameterBindings = doc.ParameterBindings;
									bool flag5 = parameterBindings.Contains(externalDefinition);
									if (flag5)
									{
										InstanceBinding instanceBinding = parameterBindings.get_Item(externalDefinition) as InstanceBinding;
										bool flag6 = instanceBinding != null && !instanceBinding.Categories.Contains(category);
										if (flag6)
										{
											instanceBinding.Categories.Insert(category);
											parameterBindings.ReInsert(externalDefinition, instanceBinding, GroupTypeId.Materials);
										}
									}
									else
									{
										InstanceBinding item = app.Create.NewInstanceBinding(categorySet);
										parameterBindings.Insert(externalDefinition, item, GroupTypeId.Materials);
									}
									BuiltInCategory category2 = isWall ? BuiltInCategory.OST_Walls : BuiltInCategory.OST_Floors;
									FilteredElementCollector filteredElementCollector = new FilteredElementCollector(doc).OfCategory(category2).WhereElementIsNotElementType();
									foreach (Element current in filteredElementCollector)
									{
										bool flag7 = !this.HasFinishLayer(current);
										if (!flag7)
										{
										Parameter parameter = current.LookupParameter("Tile_Joint_Width");
											bool flag8 = parameter != null && !parameter.IsReadOnly;
											if (flag8)
											{
												bool flag9 = !parameter.HasValue;
												if (flag9)
												{
													bool isLength = false;
													try
													{
														var propParameterType = parameter.Definition.GetType().GetProperty("ParameterType");
														if (propParameterType != null)
														{
															var val = propParameterType.GetValue(parameter.Definition).ToString();
															isLength = val.Equals("Length", StringComparison.OrdinalIgnoreCase);
														}
														else
														{
															var methodGetDataType = parameter.Definition.GetType().GetMethod("GetDataType");
															if (methodGetDataType != null)
															{
																var forgeTypeId = methodGetDataType.Invoke(parameter.Definition, null);
																if (forgeTypeId != null)
																{
																	var propTypeId = forgeTypeId.GetType().GetProperty("TypeId");
																	if (propTypeId != null)
																	{
																		string typeId = propTypeId.GetValue(forgeTypeId) as string;
																		isLength = typeId != null && typeId.IndexOf("length", StringComparison.OrdinalIgnoreCase) >= 0;
																	}
																}
															}
														}
													}
													catch
													{
														isLength = false;
													}

													if (isLength)
													{
														parameter.Set(0.00984251968503937); // Length 類型寫入 3mm 轉 feet
													}
													else
													{
														parameter.Set(3.0); // Number 類型直接寫入 3.0 (mm)
													}
													num++;
												}
											}
										}
									}
									transaction.Commit();
								}
								this.ViewModel.SetStatus(string.Format("裝修{0}縫寬參數設定 3mm 完成 (共更新 {1} 個元素)。", text, num));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this.ViewModel.SetStatus("設定縫寬參數失敗: " + ex.Message);
			}
			finally
			{
				app.SharedParametersFilename = sharedParametersFilename;
			}
		}
		private void DoDeleteTiles(Document doc)
		{
			try
			{
				int num = 0;
				using (Transaction transaction = new Transaction(doc, "磁磚配置系統操作"))
				{
					transaction.Start();
					HashSet<ElementId> hashSet = new HashSet<ElementId>();
					using (IEnumerator<Element> enumerator = new FilteredElementCollector(doc).OfClass(typeof(Material)).GetEnumerator())
					{
						while (enumerator.MoveNext())
						{
							Material material = (Material)enumerator.Current;
							bool flag = material.Name.StartsWith("TileMAT_", StringComparison.OrdinalIgnoreCase);
							if (flag)
							{
								hashSet.Add(material.Id);
							}
						}
					}
					bool flag2 = hashSet.Count > 0;
					if (flag2)
					{
						foreach (Element current in new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType())
						{
							num += this.RemovePaintFromElement(doc, current, hashSet);
						}
						foreach (Element current2 in new FilteredElementCollector(doc).OfClass(typeof(Floor)).WhereElementIsNotElementType())
						{
							num += this.RemovePaintFromElement(doc, current2, hashSet);
						}
					}
					List<Element> list = new List<Element>();
					list.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().ToElements());
					list.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Floor)).WhereElementIsNotElementType().ToElements());
					list.AddRange(new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).WhereElementIsNotElementType().ToElements());
					List<ElementId> list2 = new List<ElementId>();
					foreach (Element current3 in list)
					{
						Parameter parameter = current3.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
						string text = (parameter != null) ? parameter.AsString() : null;
						bool flag3 = !string.IsNullOrEmpty(text) && (text.Contains("Tile_ID:") || text.StartsWith("Tile_"));
						if (flag3)
						{
							list2.Add(current3.Id);
						}
					}
					bool flag4 = list2.Count > 0;
					if (flag4)
					{
						doc.Delete(list2);
					}
					transaction.Commit();
				}
				this.ViewModel.SetStatus(string.Format("已清除所有設定，共刪除 {0} 個磁磚元素或樣式。", num));
			}
			catch (Exception ex)
			{
				this.ViewModel.SetStatus("磁磚配置系統作業" + ex.Message);
			}
		}
		private void DoConfirmMaterialPat(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("請選取已繪製填充線的面。");
				Reference reference = null;
				try
				{
					reference = uidoc.Selection.PickObject(ObjectType.Face, "請選取已繪製填充線的面。");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					this.ViewModel.SetStatus("處理中...");
					return;
				}
				bool flag = reference == null;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					Element element = doc.GetElement(reference.ElementId);
					Face face = ((element != null) ? element.GetGeometryObjectFromReference(reference) : null) as Face;
					bool flag2 = face == null || !(face is PlanarFace);
					if (flag2)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						bool flag3 = !doc.IsPainted(element.Id, face);
						if (flag3)
						{
							this.ViewModel.SetStatus("處理中...");
						}
						else
						{
							ElementId paintedMaterial = doc.GetPaintedMaterial(element.Id, face);
							bool flag4 = paintedMaterial == ElementId.InvalidElementId;
							if (flag4)
							{
								this.ViewModel.SetStatus("處理中...");
							}
							else
							{
								Material material = doc.GetElement(paintedMaterial) as Material;
								bool flag5 = material == null || !material.Name.StartsWith("TileMAT_", StringComparison.OrdinalIgnoreCase);
								if (flag5)
								{
									this.ViewModel.SetStatus("處理中...");
								}
								else
								{
									TilePatternParams p = new TilePatternParams
									{
										Style = TilePatternStyle.Stack,
										TileWidth = this.ViewModel.TileWidth,
										TileHeight = this.ViewModel.TileHeight,
										JointWidth = this.ViewModel.JointWidth,
										Thickness = this.ViewModel.TileThickness
									};
									ElementId hostFinishMaterial = this.GetHostFinishMaterial(element);
									bool flag6 = hostFinishMaterial == ElementId.InvalidElementId;
									if (flag6)
									{
										this.ViewModel.SetStatus("處理中...");
									}
									else
									{
										using (Transaction transaction = new Transaction(doc, "磁磚配置系統操作"))
										{
											transaction.Start();
											doc.RemovePaint(element.Id, face);
											Material material2 = doc.GetElement(hostFinishMaterial) as Material;
											bool flag7 = material2 != null;
											if (flag7)
											{
												ElementId orCreateTilePatternElement = this.GetOrCreateTilePatternElement(doc, p);
												bool flag8 = orCreateTilePatternElement != ElementId.InvalidElementId;
												if (flag8)
												{
													material2.SurfaceForegroundPatternId = orCreateTilePatternElement;
													material2.SurfaceForegroundPatternColor = new Color(80, 80, 80);
												}
											}
											transaction.Commit();
										}
										uidoc.RefreshActiveView();
										this.ViewModel.SetStatus("處理中...");
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				TaskDialog.Show("磁磚配置系統作業", ex.ToString());
				this.ViewModel.SetStatus("磁磚配置系統作業" + ex.Message);
			}
		}
		private void DoGenerate3DTiles(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("3D 磁磚");
				Reference reference = null;
				try
				{
					reference = uidoc.Selection.PickObject(ObjectType.Face, "請點選已繪製填充線的牆面。");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					this.ViewModel.SetStatus("處理中...");
					return;
				}
				bool flag = reference == null;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					Element element = doc.GetElement(reference.ElementId);
					Face face = ((element != null) ? element.GetGeometryObjectFromReference(reference) : null) as Face;
					PlanarFace planarFace = null;
					bool arg_9E_0;
					if (!(face == null))
					{
						planarFace = (face as PlanarFace);
						arg_9E_0 = (planarFace == null);
					}
					else
					{
						arg_9E_0 = true;
					}
					bool flag2 = arg_9E_0;
					if (flag2)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						Wall wall = element as Wall;
						bool flag3 = wall == null;
						if (flag3)
						{
							this.ViewModel.SetStatus("處理中...");
						}
						else
						{
							ElementId elementId = planarFace.MaterialElementId;
							bool flag4 = elementId == ElementId.InvalidElementId;
							if (flag4)
							{
								elementId = this.GetHostFinishMaterial(element);
							}
							bool flag5 = elementId == ElementId.InvalidElementId;
							if (flag5)
							{
								this.ViewModel.SetStatus("處理中...");
							}
							else
							{
								Material material = doc.GetElement(elementId) as Material;
								bool flag6 = material == null;
								if (flag6)
								{
									this.ViewModel.SetStatus("處理中...");
								}
								else
								{
									TilePatternParams tilePatternParams = new TilePatternParams
									{
										Style = TilePatternStyle.Stack,
										TileWidth = this.ViewModel.TileWidth,
										TileHeight = this.ViewModel.TileHeight,
										JointWidth = this.ViewModel.JointWidth,
										Thickness = this.ViewModel.TileThickness
									};
									double wallThick = tilePatternParams.Thickness;
									if (elementId != ElementId.InvalidElementId)
									{
										Material mat = doc.GetElement(elementId) as Material;
										if (mat != null && TileDimensionDetector.TryParseThicknessFromNameFlexible(mat.Name, out double tFromMat))
										{
											wallThick = tFromMat;
										}
										else
										{
											wallThick = TileDimensionDetector.DetectThickness(element, doc, wallThick);
										}
									}
									else
									{
										wallThick = TileDimensionDetector.DetectThickness(element, doc, wallThick);
									}
									tilePatternParams.Thickness = wallThick;
									SurfaceTileLayoutData surfaceTileLayoutData = null;
									string uniqueId = element.UniqueId;
									string roomId = "ManualPick";
									string anchorId = "ManualPick";
									Room room = this.FindRoomAtFace(doc, planarFace);
									bool flag7 = room != null;
									if (flag7)
									{
										roomId = room.Number + " - " + room.Name;
										anchorId = room.Number;
									}
									double num = 1.7976931348623157E+308;
									double num2 = -1.7976931348623157E+308;
									foreach (EdgeArray edgeArray in planarFace.EdgeLoops)
									{
										foreach (Edge edge in edgeArray)
										{
											foreach (XYZ current in edge.Tessellate())
											{
												bool flag8 = current.Z < num;
												if (flag8)
												{
													num = current.Z;
												}
												bool flag9 = current.Z > num2;
												if (flag9)
												{
													num2 = current.Z;
												}
											}
										}
									}
									double startHeightMm = 0.0;
									double endHeightMm = (num2 - num) * 304.8;
									FaceDrivenGeometryAnalyzer faceDrivenGeometryAnalyzer = new FaceDrivenGeometryAnalyzer(doc, this.FindAny3DView(doc));
									WallSurfaceGeometry wallSurfaceGeometry = faceDrivenGeometryAnalyzer.BuildWallGeometryFromFace(wall, planarFace, reference, startHeightMm, endHeightMm);
									bool flag10 = wallSurfaceGeometry != null;
									if (flag10)
									{
										TileLayoutEngine tileLayoutEngine = new TileLayoutEngine();
										surfaceTileLayoutData = tileLayoutEngine.LayoutWall(wallSurfaceGeometry, null, tilePatternParams, anchorId, roomId, 0, 0.0, 0.0);
									}
									bool flag11 = surfaceTileLayoutData == null || surfaceTileLayoutData.Tiles.Count == 0;
									if (flag11)
									{
										this.ViewModel.SetStatus("處理中...");
									}
									else
									{
										SharedParameterHelper.RegisterAndBindParameters(doc);
										ElementId elementId2 = element.LevelId;
										bool flag12 = elementId2 == ElementId.InvalidElementId;
										if (flag12)
										{
											Parameter parameter = element.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
											bool flag13 = parameter != null;
											if (flag13)
											{
												elementId2 = parameter.AsElementId();
											}
										}
										bool flag14 = elementId2 == ElementId.InvalidElementId;
										if (flag14)
										{
											elementId2 = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElementId();
										}
										int num3 = 0;
										List<ElementId> list = new List<ElementId>();
										using (Transaction transaction = new Transaction(doc, "3D 磁磚"))
										{
											transaction.Start();
											GeometryGenerator geometryGenerator = new GeometryGenerator(doc);
											List<ElementId> list2 = new List<ElementId>();
											foreach (TileData current2 in surfaceTileLayoutData.Tiles)
											{
												Element element2 = geometryGenerator.GenerateTileElement(current2, elementId, elementId2);
												bool flag15 = element2 != null;
												if (flag15)
												{
													list2.Add(element2.Id);
													this._lastCreatedTileIds.Add(element2.Id);
													num3++;
												}
											}
											bool flag16 = list2.Count > 0;
											if (flag16)
											{
												try
												{
													Autodesk.Revit.DB.Group group = doc.Create.NewGroup(list2);
													string str = DateTime.Now.ToString("yyyyMMdd_HHmmss");
													string name = "TileGroup_" + element.Name + "_" + str;
													name = this.CleanGroupName(name);
													try
													{
														group.GroupType.Name = name;
													}
													catch
													{
														group.GroupType.Name = "TileGroup_" + Guid.NewGuid().ToString().Substring(0, 8);
													}
												}
												catch (Exception ex)
												{
													Debug.WriteLine("磁磚配置系統作業" + ex.Message);
												}
											}
											transaction.Commit();
										}
										uidoc.RefreshActiveView();
										this.ViewModel.SetStatus(string.Format("成功生成了 {0} 塊 3D 磁磚。", num3));
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex2)
			{
				TaskDialog.Show("3D 磁磚", ex2.ToString());
				this.ViewModel.SetStatus("3D 磁磚" + ex2.Message);
			}
		}
		private void DoGenerate3DFloorTiles(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("3D 磁磚");
				Reference reference = null;
				try
				{
					reference = uidoc.Selection.PickObject(ObjectType.Face, "請點選已繪製填充線的地板完成面。");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					this.ViewModel.SetStatus("處理中...");
					return;
				}
				bool flag = reference == null;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					Element element = doc.GetElement(reference.ElementId);
					Face face = ((element != null) ? element.GetGeometryObjectFromReference(reference) : null) as Face;
					PlanarFace planarFace = null;
					bool arg_9E_0;
					if (!(face == null))
					{
						planarFace = (face as PlanarFace);
						arg_9E_0 = (planarFace == null);
					}
					else
					{
						arg_9E_0 = true;
					}
					bool flag2 = arg_9E_0;
					if (flag2)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						Floor floor = element as Floor;
						bool flag3 = floor == null;
						if (flag3)
						{
							this.ViewModel.SetStatus("處理中...");
						}
						else
						{
							ElementId elementId = planarFace.MaterialElementId;
							bool flag4 = elementId == ElementId.InvalidElementId;
							if (flag4)
							{
								elementId = this.GetHostFinishMaterial(element);
							}
							bool flag5 = elementId == ElementId.InvalidElementId;
							if (flag5)
							{
								GeometryGenerator geometryGenerator = new GeometryGenerator(doc);
								elementId = geometryGenerator.GetOrCreateTileMaterial("Tile_Default_Material", new Color(245, 245, 245));
							}
							TilePatternParams tilePatternParams = new TilePatternParams
							{
								Style = TilePatternStyle.Stack,
								TileWidth = this.ViewModel.TileWidth,
								TileHeight = this.ViewModel.TileHeight,
								JointWidth = this.ViewModel.JointWidth,
								Thickness = this.ViewModel.TileThickness
							};
							double floorThick = tilePatternParams.Thickness;
							if (elementId != ElementId.InvalidElementId)
							{
								Material mat = doc.GetElement(elementId) as Material;
								if (mat != null && TileDimensionDetector.TryParseThicknessFromNameFlexible(mat.Name, out double tFromMat))
								{
									floorThick = tFromMat;
								}
								else
								{
									floorThick = TileDimensionDetector.DetectThickness(element, doc, floorThick);
								}
							}
							else
							{
								floorThick = TileDimensionDetector.DetectThickness(element, doc, floorThick);
							}
							tilePatternParams.Thickness = floorThick;
							SurfaceTileLayoutData surfaceTileLayoutData = null;
							string uniqueId = element.UniqueId;
							string roomId = "ManualPick";
							string anchorId = "ManualPick";
							Room room = this.FindRoomAtFace(doc, planarFace);
							bool flag6 = room != null;
							if (flag6)
							{
								roomId = room.Number + " - " + room.Name;
								anchorId = room.Number;
							}
							FaceDrivenGeometryAnalyzer faceDrivenGeometryAnalyzer = new FaceDrivenGeometryAnalyzer(doc, this.FindAny3DView(doc));
							FloorSurfaceGeometry floorSurfaceGeometry = faceDrivenGeometryAnalyzer.BuildFloorGeometryFromFace(floor, planarFace, reference);
							bool flag7 = floorSurfaceGeometry != null;
							if (flag7)
							{
								TileLayoutEngine tileLayoutEngine = new TileLayoutEngine();
								RoomLocalCoordinate localCoord = new RoomLocalCoordinate(floorSurfaceGeometry.Origin, floorSurfaceGeometry.XVector, floorSurfaceGeometry.YVector);
								surfaceTileLayoutData = tileLayoutEngine.LayoutFloor(floorSurfaceGeometry.BoundaryLoops, localCoord, tilePatternParams, anchorId, roomId, uniqueId, 0.0, 0.0);
								bool flag8 = surfaceTileLayoutData == null || surfaceTileLayoutData.Tiles.Count == 0;
								if (flag8)
								{
									RoomLocalCoordinate localCoord2 = new RoomLocalCoordinate(floorSurfaceGeometry.Origin, XYZ.BasisX, XYZ.BasisY);
									surfaceTileLayoutData = tileLayoutEngine.LayoutFloor(floorSurfaceGeometry.BoundaryLoops, localCoord2, tilePatternParams, anchorId, roomId, uniqueId, 0.0, 0.0);
								}
							}
							bool flag9 = surfaceTileLayoutData == null || surfaceTileLayoutData.Tiles.Count == 0;
							if (flag9)
							{
								this.ViewModel.SetStatus("處理中...");
							}
							else
							{
								SharedParameterHelper.RegisterAndBindParameters(doc);
								ElementId elementId2 = element.LevelId;
								bool flag10 = elementId2 == ElementId.InvalidElementId;
								if (flag10)
								{
									elementId2 = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElementId();
								}
								int num = 0;
								using (Transaction transaction = new Transaction(doc, "3D 磁磚"))
								{
									transaction.Start();
									GeometryGenerator geometryGenerator2 = new GeometryGenerator(doc);
									List<ElementId> list = new List<ElementId>();
									foreach (TileData current in surfaceTileLayoutData.Tiles)
									{
										Element element2 = geometryGenerator2.GenerateTileElement(current, elementId, elementId2);
										bool flag11 = element2 != null;
										if (flag11)
										{
											list.Add(element2.Id);
											this.createdTileIds.Add(element2.Id);
											num++;
										}
									}
									bool flag12 = list.Count > 0;
									if (flag12)
									{
										try
										{
											Autodesk.Revit.DB.Group group = doc.Create.NewGroup(list);
											string str = DateTime.Now.ToString("yyyyMMdd_HHmmss");
											string name = "TileGroup_" + element.Name + "_" + str;
											name = this.CleanGroupName(name);
											try
											{
												group.GroupType.Name = name;
											}
											catch
											{
												group.GroupType.Name = "TileGroup_" + Guid.NewGuid().ToString().Substring(0, 8);
											}
										}
										catch (Exception ex)
										{
											Debug.WriteLine("磁磚配置系統作業" + ex.Message);
										}
									}
									transaction.Commit();
								}
								uidoc.RefreshActiveView();
								this.ViewModel.SetStatus(string.Format("生成成功：地面 {0} 實體 {1} 塊 3D 磁磚...", element.Name, num));
							}
						}
					}
				}
			}
			catch (Exception ex2)
			{
				this.ViewModel.SetStatus("3D 磁磚" + ex2.Message);
			}
		}
		private void DoCalculatePlaneQuantity(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("請選取已繪製填充線的面。");
				IList<Reference> list = null;
				try
				{
					list = uidoc.Selection.PickObjects(ObjectType.Face, "請選取已繪製填充線的面。");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					this.ViewModel.SetStatus("處理中...");
					return;
				}
				bool flag = list == null || list.Count == 0;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					RoomTileLayoutData roomTileLayoutData = new RoomTileLayoutData
					{
						Room_ID = "MultiSelect",
						Anchor_ID = "MultiSelect"
					};
					List<string> list2 = new List<string>();
					foreach (Reference current in list)
					{
						Element element = doc.GetElement(current.ElementId);
						Face face = ((element != null) ? element.GetGeometryObjectFromReference(current) : null) as Face;
						PlanarFace planarFace = null;
						bool arg_EF_0;
						if (!(face == null))
						{
							planarFace = (face as PlanarFace);
							arg_EF_0 = (planarFace == null);
						}
						else
						{
							arg_EF_0 = true;
						}
						bool flag2 = arg_EF_0;
						if (!flag2)
						{
							ElementId hostFinishMaterial = this.GetHostFinishMaterial(element);
							bool flag3 = hostFinishMaterial == ElementId.InvalidElementId;
							if (!flag3)
							{
								Material material = doc.GetElement(hostFinishMaterial) as Material;
								bool flag4 = material == null;
								if (!flag4)
								{
									TilePatternParams tilePatternParams = new TilePatternParams
									{
										Style = TilePatternStyle.Stack,
										TileWidth = this.ViewModel.TileWidth,
										TileHeight = this.ViewModel.TileHeight,
										JointWidth = this.ViewModel.JointWidth,
										Thickness = this.ViewModel.TileThickness
									};
									tilePatternParams.Thickness = TileDimensionDetector.DetectThickness(element, doc, tilePatternParams.Thickness);
									SurfaceTileLayoutData surfaceTileLayoutData = null;
									string uniqueId = element.UniqueId;
									Floor floor = element as Floor;
									bool flag5 = floor != null;
									if (flag5)
									{
										FaceDrivenGeometryAnalyzer faceDrivenGeometryAnalyzer = new FaceDrivenGeometryAnalyzer(doc, this.FindAny3DView(doc));
										FloorSurfaceGeometry floorSurfaceGeometry = faceDrivenGeometryAnalyzer.BuildFloorGeometryFromFace(floor, planarFace, current);
										bool flag6 = floorSurfaceGeometry != null;
										if (flag6)
										{
											TileLayoutEngine tileLayoutEngine = new TileLayoutEngine();
											RoomLocalCoordinate localCoord = new RoomLocalCoordinate(floorSurfaceGeometry.Origin, floorSurfaceGeometry.XVector, floorSurfaceGeometry.YVector);
											surfaceTileLayoutData = tileLayoutEngine.LayoutFloor(floorSurfaceGeometry.BoundaryLoops, localCoord, tilePatternParams, "MultiSelect", "MultiSelect", uniqueId, 0.0, 0.0);
											bool flag7 = surfaceTileLayoutData == null || surfaceTileLayoutData.Tiles.Count == 0;
											if (flag7)
											{
												RoomLocalCoordinate localCoord2 = new RoomLocalCoordinate(floorSurfaceGeometry.Origin, XYZ.BasisX, XYZ.BasisY);
												surfaceTileLayoutData = tileLayoutEngine.LayoutFloor(floorSurfaceGeometry.BoundaryLoops, localCoord2, tilePatternParams, "MultiSelect", "MultiSelect", uniqueId, 0.0, 0.0);
											}
										}
									}
									else
									{
										Wall wall = element as Wall;
										bool flag8 = wall != null;
										if (flag8)
										{
											double num = 1.7976931348623157E+308;
											double num2 = -1.7976931348623157E+308;
											foreach (EdgeArray edgeArray in planarFace.EdgeLoops)
											{
												foreach (Edge edge in edgeArray)
												{
													foreach (XYZ current2 in edge.Tessellate())
													{
														bool flag9 = current2.Z < num;
														if (flag9)
														{
															num = current2.Z;
														}
														bool flag10 = current2.Z > num2;
														if (flag10)
														{
															num2 = current2.Z;
														}
													}
												}
											}
											double startHeightMm = 0.0;
											double endHeightMm = (num2 - num) * 304.8;
											FaceDrivenGeometryAnalyzer faceDrivenGeometryAnalyzer2 = new FaceDrivenGeometryAnalyzer(doc, this.FindAny3DView(doc));
											WallSurfaceGeometry wallSurfaceGeometry = faceDrivenGeometryAnalyzer2.BuildWallGeometryFromFace(wall, planarFace, current, startHeightMm, endHeightMm);
											bool flag11 = wallSurfaceGeometry != null;
											if (flag11)
											{
												TileLayoutEngine tileLayoutEngine2 = new TileLayoutEngine();
												surfaceTileLayoutData = tileLayoutEngine2.LayoutWall(wallSurfaceGeometry, null, tilePatternParams, "MultiSelect", "MultiSelect", roomTileLayoutData.Surfaces.Count, 0.0, 0.0);
											}
										}
									}
									bool flag12 = surfaceTileLayoutData != null && surfaceTileLayoutData.Tiles.Count > 0;
									if (flag12)
									{
										roomTileLayoutData.Surfaces.Add(surfaceTileLayoutData);
										list2.Add(string.Format("  • {0}: {1} (規格: {2:F0}x{3:F0}mm) - 平面估算磁磚數: {4} 塊", new object[]
										{
											(element is Floor) ? "地坪" : "牆面",
											element.Name,
											tilePatternParams.TileWidth,
											tilePatternParams.TileHeight,
											surfaceTileLayoutData.Tiles.Count
										}));
									}
								}
							}
						}
					}
					bool flag13 = roomTileLayoutData.Surfaces.Count == 0;
					if (flag13)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						QuantityEngine quantityEngine = new QuantityEngine();
						RoomTileStatistics roomTileStatistics = quantityEngine.CalculateStatistics(roomTileLayoutData);
						string mainInstruction = string.Concat(new string[]
						{
							"平面幾何統計報告\n\n已選取並計算 ",
							string.Format("共 {0} 個表面：\n", roomTileLayoutData.Surfaces.Count),
							string.Join("\n", list2),
							"\n\n統計結果總計：\n",
							string.Format("  • 估算總磚數: {0} 塊\n", roomTileStatistics.TotalTileCount),
							string.Format("  • 整磚數量: {0} 塊\n", roomTileStatistics.FullTileCount),
							string.Format("  • 裁切/邊磚數量: {0} 塊\n", roomTileStatistics.CutTileCount + roomTileStatistics.BorderTileCount),
							string.Format("    - 其中裁切磚: {0} 塊\n", roomTileStatistics.CutTileCount),
							string.Format("    - 其中邊磚: {0} 塊\n", roomTileStatistics.BorderTileCount),
							"\n面積與損耗：\n",
							string.Format("  • 實際鋪貼面積: {0:F2} ㎡\n", roomTileStatistics.TotalArea),
							string.Format("  • 損耗磁磚面積: {0:F2} ㎡\n", roomTileStatistics.WastedArea),
							string.Format("  • 估算損耗率: {0:F1}%\n", roomTileStatistics.WasteRatio)
						});
						TaskDialog.Show("平面幾何統計", mainInstruction);
						this.ViewModel.SetStatus(string.Format("平面幾何統計完成。總磚數: {0} 塊, 損耗率: {1:F1}%", roomTileStatistics.TotalTileCount, roomTileStatistics.WasteRatio));
					}
				}
			}
			catch (Exception ex)
			{
				TaskDialog.Show("平面幾何統計錯誤", ex.ToString());
				this.ViewModel.SetStatus("平面幾何統計失敗: " + ex.Message);
			}
		}
		private void DoCalculate3DQuantity(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("請選取要進行 3D 統計的房間。");
				IList<Reference> list = null;
				try
				{
					list = uidoc.Selection.PickObjects(ObjectType.Element, new RoomSelectionFilter(), "請選取要進行 3D 統計的房間。");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					this.ViewModel.SetStatus("處理中...");
					return;
				}
				bool flag = list == null || list.Count == 0;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					HashSet<string> hashSet = new HashSet<string>();
					HashSet<string> hashSet2 = new HashSet<string>();
					Dictionary<string, Room> dictionary = new Dictionary<string, Room>();
					foreach (Reference current in list)
					{
						Room room = doc.GetElement(current.ElementId) as Room;
						bool flag2 = room != null;
						if (flag2)
						{
							hashSet.Add(room.UniqueId);
							dictionary[room.UniqueId] = room;
							hashSet2.Add(room.Number + " - " + room.Name);
						}
					}
					List<Element> list2 = new List<Element>();
					list2.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().ToElements());
					list2.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Floor)).WhereElementIsNotElementType().ToElements());
					list2.AddRange(new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).WhereElementIsNotElementType().ToElements());
					Dictionary<string, List<Element>> dictionary2 = new Dictionary<string, List<Element>>();
					foreach (Element current2 in list2)
					{
						Parameter parameter = current2.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
						string text = (parameter != null) ? parameter.AsString() : null;
						bool flag3 = string.IsNullOrEmpty(text) || !text.Contains("Tile_ID:");
						if (!flag3)
						{
							string text2 = null;
							Parameter parameter2 = current2.LookupParameter("Room_ID");
							bool flag4 = parameter2 != null && parameter2.HasValue;
							if (flag4)
							{
								text2 = parameter2.AsString();
							}
							bool flag5 = string.IsNullOrEmpty(text2);
							if (flag5)
							{
								text2 = this.ParseCommentValue(text, "Room_ID");
							}
							bool flag6 = string.IsNullOrEmpty(text2) || text2 == "ManualPick";
							if (flag6)
							{
								string text3 = this.DetectRoomIdOfTile(doc, text);
								bool flag7 = text3 != "ManualPick";
								if (flag7)
								{
									Room room2 = doc.GetElement(text3) as Room;
									bool flag8 = room2 != null;
									if (flag8)
									{
										text2 = room2.Number + " - " + room2.Name;
									}
									else
									{
										text2 = text3;
									}
								}
							}
							string text4 = null;
							bool flag9 = hashSet.Contains(text2);
							if (flag9)
							{
								Room room3 = doc.GetElement(text2) as Room;
								bool flag10 = room3 != null;
								if (flag10)
								{
									text4 = room3.Number + " - " + room3.Name;
								}
								else
								{
									text4 = text2;
								}
							}
							else
							{
								bool flag11 = hashSet2.Contains(text2);
								if (flag11)
								{
									text4 = text2;
								}
							}
							bool flag12 = !string.IsNullOrEmpty(text4);
							if (flag12)
							{
								bool flag13 = !dictionary2.ContainsKey(text4);
								if (flag13)
								{
									dictionary2[text4] = new List<Element>();
								}
								dictionary2[text4].Add(current2);
							}
						}
					}
					StringBuilder stringBuilder = new StringBuilder();
					stringBuilder.AppendLine("3D 磁磚配置統計報告：");
					stringBuilder.AppendLine(string.Format("選取房間總數：{0} 間", dictionary.Count));
					stringBuilder.AppendLine("====================================");
					int num = 0;
					double num2 = 0.0;
					foreach (KeyValuePair<string, Room> current3 in dictionary)
					{
						string key = current3.Key;
						Room value = current3.Value;
						stringBuilder.AppendLine(string.Concat(new string[]
						{
							"\ud83c\udfe0 房間：",
							value.Name,
							" (",
							value.Number,
							")"
						}));
						string key2 = value.Number + " - " + value.Name;
						bool flag14 = !dictionary2.ContainsKey(key2) || dictionary2[key2].Count == 0;
						if (flag14)
						{
							stringBuilder.AppendLine("  (尚未在此房間生成 3D 磁磚實體)");
							stringBuilder.AppendLine("------------------------------------");
						}
						else
						{
							List<Element> list3 = dictionary2[key2];
							int num3 = 0;
							int num4 = 0;
							int num5 = 0;
							int num6 = 0;
							double num7 = 0.0;
							double num8 = 0.0;
							foreach (Element current4 in list3)
							{
								Parameter parameter3 = current4.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
								string comments = (parameter3 != null) ? parameter3.AsString() : null;
								string text5 = this.ParseCommentValue(comments, "Type");
								double num9 = 0.0;
								Parameter parameter4 = current4.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
								bool flag15 = parameter4 != null && parameter4.HasValue;
								if (flag15)
								{
									num9 = parameter4.AsDouble() * 0.09290304;
								}
								else
								{
									string s = this.ParseCommentValue(comments, "Area");
									double.TryParse(s, out num9);
								}
								num3++;
								num7 += num9;
								bool flag16 = text5.Equals("Full", StringComparison.OrdinalIgnoreCase);
								if (flag16)
								{
									num4++;
								}
								else
								{
									bool flag17 = text5.Equals("Cut", StringComparison.OrdinalIgnoreCase);
									if (flag17)
									{
										num5++;
									}
									else
									{
										bool flag18 = text5.Equals("Border", StringComparison.OrdinalIgnoreCase);
										if (flag18)
										{
											num6++;
										}
									}
								}
								bool flag19 = num8 < 1E-06;
								if (flag19)
								{
									Parameter parameter5 = current4.LookupParameter("Tile_Width");
									Parameter parameter6 = current4.LookupParameter("Tile_Height");
									bool flag20 = parameter5 != null && parameter6 != null;
									if (flag20)
									{
										double num10 = parameter5.AsDouble() * 304.8;
										double num11 = parameter6.AsDouble() * 304.8;
										num8 = num10 * num11 / 1000000.0;
									}
								}
							}
							bool flag21 = num8 < 1E-06;
							if (flag21)
							{
								num8 = 0.09;
							}
							double num12 = (double)num3 * num8;
							double num13 = Math.Max(0.0, num12 - num7);
							double num14 = (num12 > 1E-06) ? (num13 / num12 * 100.0) : 0.0;
							num += num3;
							num2 += num7;
							stringBuilder.AppendLine(string.Format("  • 3D 實體總數: {0} 塊 (整磚: {1} 塊, 裁切/邊磚: {2} 塊)", num3, num4, num5 + num6));
							stringBuilder.AppendLine(string.Format("  • 鋪貼面積: {0:F2} ㎡ / 磁磚總面積: {1:F2} ㎡", num7, num12));
							stringBuilder.AppendLine(string.Format("  • 實體損耗率: {0:F1}%", num14));
							stringBuilder.AppendLine("------------------------------------");
						}
					}
					stringBuilder.AppendLine("====================================");
					stringBuilder.AppendLine(string.Format("總計 3D 實體總數: {0} 塊", num));
					stringBuilder.AppendLine(string.Format("總計實際鋪貼面積: {0:F2} ㎡", num2));
					TaskDialog.Show("3D 實體數量統計 (房間分組)", stringBuilder.ToString());
					this.ViewModel.SetStatus(string.Format("3D 實體統計完成。總實體數: {0} 塊，總面積: {1:F2} ㎡", num, num2));
				}
			}
			catch (Exception ex)
			{
				TaskDialog.Show("3D 磁磚統計錯誤", ex.ToString());
				this.ViewModel.SetStatus("3D 磁磚統計失敗: " + ex.Message);
			}
		}
		private Room FindRoomAtFace(Document doc, PlanarFace face)
		{
			Room result;
			try
			{
				XYZ origin = face.Origin;
				XYZ faceNormal = face.FaceNormal;
				XYZ point = origin + faceNormal * 0.3;
				XYZ point2 = origin - faceNormal * 0.3;
				FilteredElementCollector filteredElementCollector = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms);
				foreach (Element current in filteredElementCollector)
				{
					Room room = current as Room;
					bool flag = room != null;
					if (flag)
					{
						bool flag2 = room.IsPointInRoom(point) || room.IsPointInRoom(point2);
						if (flag2)
						{
							result = room;
							return result;
						}
					}
				}
			}
			catch
			{
			}
			result = null;
			return result;
		}
		private string DetectRoomIdOfTile(Document doc, string comments)
		{
			string result;
			try
			{
				string text = this.ParseCommentValue(comments, "BoundaryPoints");
				bool flag = string.IsNullOrEmpty(text);
				if (flag)
				{
					result = "ManualPick";
					return result;
				}
				List<XYZ> list = new List<XYZ>();
				string[] array = text.Split(new char[]
				{
					';'
				}, StringSplitOptions.RemoveEmptyEntries);
				string[] array2 = array;
				for (int i = 0; i < array2.Length; i++)
				{
					string text2 = array2[i];
					string[] array3 = text2.Split(new char[]
					{
						','
					});
					bool flag2 = array3.Length == 3;
					if (flag2)
					{
						double x = 0.0; double y = 0.0; double z = 0.0;
						bool flag3 = double.TryParse(array3[0], out x) && double.TryParse(array3[1], out y) && double.TryParse(array3[2], out z);
						if (flag3)
						{
							list.Add(new XYZ(x, y, z));
						}
					}
				}
				bool flag4 = list.Count < 3;
				if (flag4)
				{
					result = "ManualPick";
					return result;
				}
				IEnumerable<XYZ> arg_100_0 = list;
				Func<XYZ, double> arg_100_1;
				if ((arg_100_1 = TileSyncEventHandler.__c.__9__22_0) == null)
				{
					arg_100_1 = (TileSyncEventHandler.__c.__9__22_0 = new Func<XYZ, double>(TileSyncEventHandler.__c.__9.__DetectRoomIdOfTile_b__22_0));
				}
				double arg_14F_0 = arg_100_0.Average(arg_100_1);
				IEnumerable<XYZ> arg_125_0 = list;
				Func<XYZ, double> arg_125_1;
				if ((arg_125_1 = TileSyncEventHandler.__c.__9__22_1) == null)
				{
					arg_125_1 = (TileSyncEventHandler.__c.__9__22_1 = new Func<XYZ, double>(TileSyncEventHandler.__c.__9.__DetectRoomIdOfTile_b__22_1));
				}
				double arg_14F_1 = arg_125_0.Average(arg_125_1);
				IEnumerable<XYZ> arg_14A_0 = list;
				Func<XYZ, double> arg_14A_1;
				if ((arg_14A_1 = TileSyncEventHandler.__c.__9__22_2) == null)
				{
					arg_14A_1 = (TileSyncEventHandler.__c.__9__22_2 = new Func<XYZ, double>(TileSyncEventHandler.__c.__9.__DetectRoomIdOfTile_b__22_2));
				}
				XYZ left = new XYZ(arg_14F_0, arg_14F_1, arg_14A_0.Average(arg_14A_1));
				XYZ left2 = TileSyncEventHandler.CalculatePolygonNormal(list);
				XYZ point = left + left2 * 0.3;
				XYZ point2 = left - left2 * 0.3;
				FilteredElementCollector filteredElementCollector = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms);
				foreach (Element current in filteredElementCollector)
				{
					Room room = current as Room;
					bool flag5 = room != null;
					if (flag5)
					{
						bool flag6 = room.IsPointInRoom(point) || room.IsPointInRoom(point2);
						if (flag6)
						{
							result = room.UniqueId;
							return result;
						}
					}
				}
			}
			catch
			{
			}
			result = "ManualPick";
			return result;
		}
		private static XYZ CalculatePolygonNormal(List<XYZ> pts)
		{
			bool flag = pts.Count < 3;
			XYZ result;
			if (flag)
			{
				result = XYZ.BasisZ;
			}
			else
			{
				double num = 0.0;
				double num2 = 0.0;
				double num3 = 0.0;
				for (int i = 0; i < pts.Count; i++)
				{
					XYZ xYZ = pts[i];
					XYZ xYZ2 = pts[(i + 1) % pts.Count];
					num += (xYZ.Y - xYZ2.Y) * (xYZ.Z + xYZ2.Z);
					num2 += (xYZ.Z - xYZ2.Z) * (xYZ.X + xYZ2.X);
					num3 += (xYZ.X - xYZ2.X) * (xYZ.Y + xYZ2.Y);
				}
				XYZ xYZ3 = new XYZ(num, num2, num3);
				bool flag2 = xYZ3.GetLength() < 0.001;
				if (flag2)
				{
					result = XYZ.BasisZ;
				}
				else
				{
					result = xYZ3.Normalize();
				}
			}
			return result;
		}
		private string ParseCommentValue(string comments, string key)
		{
			if (string.IsNullOrEmpty(comments) || string.IsNullOrEmpty(key)) return string.Empty;
			string result;
			try
			{
				string[] array = comments.Split(new char[]
				{
					'|'
				});
				string[] array2 = array;
				for (int i = 0; i < array2.Length; i++)
				{
					string text = array2[i];
					bool flag = text.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase);
					if (flag)
					{
						result = text.Substring(key.Length + 1);
						return result;
					}
				}
			}
			catch
			{
			}
			result = string.Empty;
			return result;
		}
		private TilePatternParams ParseParamsFromPatName(string patName)
		{
			TilePatternParams result;
			try
			{
				Match match = Regex.Match(patName, "TilePAT_(\\d+)_(\\d+)x(\\d+)_J(\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
				bool success = match.Success;
				if (success)
				{
					TilePatternStyle style = (TilePatternStyle)int.Parse(match.Groups[1].Value);
					double tileWidth = double.Parse(match.Groups[2].Value);
					double tileHeight = double.Parse(match.Groups[3].Value);
					double jointWidth = double.Parse(match.Groups[4].Value);
					result = new TilePatternParams
					{
						Style = style,
						TileWidth = tileWidth,
						TileHeight = tileHeight,
						JointWidth = jointWidth,
						Thickness = 10.0
					};
					return result;
				}
			}
			catch
			{
			}
			result = null;
			return result;
		}
		private TilePatternParams ParseParamsFromMatName(string matName)
		{
			TilePatternParams result;
			try
			{
				Match match = Regex.Match(matName, "TileMAT_(\\d+)_(\\d+)x(\\d+)_J(\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
				bool success = match.Success;
				if (success)
				{
					TilePatternStyle style = (TilePatternStyle)int.Parse(match.Groups[1].Value);
					double tileWidth = double.Parse(match.Groups[2].Value);
					double tileHeight = double.Parse(match.Groups[3].Value);
					double jointWidth = double.Parse(match.Groups[4].Value);
					result = new TilePatternParams
					{
						Style = style,
						TileWidth = tileWidth,
						TileHeight = tileHeight,
						JointWidth = jointWidth,
						Thickness = 10.0
					};
					return result;
				}
			}
			catch
			{
			}
			result = null;
			return result;
		}
		private ElementId GetOrCreateTilePatternElement(Document doc, TilePatternParams p)
		{
			string str = string.Format("{0}_{1}x{2}_J{3:F1}", new object[]
			{
				(int)p.Style,
				(int)p.TileWidth,
				(int)p.TileHeight,
				p.JointWidth
			});
			string text = "TilePAT_" + str;
			ElementId result;
			using (IEnumerator<Element> enumerator = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					FillPatternElement fillPatternElement = (FillPatternElement)enumerator.Current;
					bool flag = fillPatternElement.Name.Equals(text, StringComparison.OrdinalIgnoreCase);
					if (flag)
					{
						result = fillPatternElement.Id;
						return result;
					}
				}
			}
			try
			{
				FillPattern fillPattern = new FillPattern(text, FillPatternTarget.Model, FillPatternHostOrientation.ToHost);
				List<FillGrid> list = this.BuildFillGrids(p);
				bool flag2 = list.Count == 0;
				if (flag2)
				{
					result = ElementId.InvalidElementId;
				}
				else
				{
					fillPattern.SetFillGrids(list);
					FillPatternElement fillPatternElement2 = FillPatternElement.Create(doc, fillPattern);
					result = fillPatternElement2.Id;
				}
			}
			catch
			{
				result = ElementId.InvalidElementId;
			}
			return result;
		}
		private ElementId GetHostFinishMaterial(Element host)
		{
			Wall wall = host as Wall;
			bool flag = wall != null;
			ElementId result;
			if (flag)
			{
				try
				{
					WallType wallType = wall.WallType;
					CompoundStructure compoundStructure = wallType.GetCompoundStructure();
					bool flag2 = compoundStructure != null;
					if (flag2)
					{
						for (int i = 0; i < compoundStructure.LayerCount; i++)
						{
							MaterialFunctionAssignment layerFunction = compoundStructure.GetLayerFunction(i);
							bool flag3 = layerFunction == MaterialFunctionAssignment.Finish1 || layerFunction == MaterialFunctionAssignment.Finish2;
							if (flag3)
							{
								ElementId materialId = compoundStructure.GetMaterialId(i);
								bool flag4 = materialId != ElementId.InvalidElementId;
								if (flag4)
								{
									result = materialId;
									return result;
								}
							}
						}
						bool flag5 = compoundStructure.LayerCount > 0;
						if (flag5)
						{
							result = compoundStructure.GetMaterialId(0);
							return result;
						}
					}
				}
				catch
				{
				}
			}
			else
			{
				Floor floor = host as Floor;
				bool flag6 = floor != null;
				if (flag6)
				{
					try
					{
						FloorType floorType = floor.FloorType;
						CompoundStructure compoundStructure2 = floorType.GetCompoundStructure();
						bool flag7 = compoundStructure2 != null;
						if (flag7)
						{
							for (int j = 0; j < compoundStructure2.LayerCount; j++)
							{
								MaterialFunctionAssignment layerFunction2 = compoundStructure2.GetLayerFunction(j);
								bool flag8 = layerFunction2 == MaterialFunctionAssignment.Finish1 || layerFunction2 == MaterialFunctionAssignment.Finish2;
								if (flag8)
								{
									ElementId materialId2 = compoundStructure2.GetMaterialId(j);
									bool flag9 = materialId2 != ElementId.InvalidElementId;
									if (flag9)
									{
										result = materialId2;
										return result;
									}
								}
							}
							bool flag10 = compoundStructure2.LayerCount > 0;
							if (flag10)
							{
								result = compoundStructure2.GetMaterialId(0);
								return result;
							}
						}
					}
					catch
					{
					}
				}
			}
			result = ElementId.InvalidElementId;
			return result;
		}
		private void PaintRoomFaces(Document doc, Room room)
		{
			View3D view3D = this.FindAny3DView(doc);
			bool flag = view3D == null;
			if (flag)
			{
				this.ViewModel.SetStatus("處理中...");
			}
			else
			{
				XYZ roomCenter = this.GetRoomCenter(room);
				RoomLocalCoordinate localCoord = new RoomLocalCoordinate(roomCenter, XYZ.BasisX, XYZ.BasisY);
				FaceDrivenGeometryAnalyzer faceDrivenGeometryAnalyzer = new FaceDrivenGeometryAnalyzer(doc, view3D);
				FloorSurfaceGeometry floorSurfaceGeometry = faceDrivenGeometryAnalyzer.ExtractFloorFace(room, roomCenter, localCoord, null);
				List<WallSurfaceGeometry> list = faceDrivenGeometryAnalyzer.ExtractWallFaces(room, roomCenter, localCoord, 0.0, faceDrivenGeometryAnalyzer.ExtractCeilingHeight(roomCenter).GetValueOrDefault(2400.0), null);
				bool flag2 = floorSurfaceGeometry == null && (list == null || list.Count == 0);
				if (flag2)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					int num = 0;
					using (Transaction transaction = new Transaction(doc, "同步房間磁磚 PAT: " + room.Name))
					{
						transaction.Start();
						bool flag3 = floorSurfaceGeometry != null;
						if (flag3)
						{
							num += this.TryPaintFace(doc, floorSurfaceGeometry.FaceReference.ElementId, floorSurfaceGeometry.FaceObject, true);
						}
						foreach (WallSurfaceGeometry current in list)
						{
							num += this.TryPaintFace(doc, current.FaceReference.ElementId, current.FaceObject, false);
						}
						transaction.Commit();
					}
					doc.GetElement(room.Id);
					this.ViewModel.SetStatus(string.Format("磁磚 PAT 填充樣式", new object[]
					{
						room.Name,
						(floorSurfaceGeometry != null) ? 1 : 0,
						(list != null) ? list.Count : 0,
						num
					}));
				}
			}
		}
		private int TryPaintFace(Document doc, ElementId hostId, Face face, bool isFloor)
		{
			int result;
			try
			{
				Element element = doc.GetElement(hostId);
				bool flag = element == null;
				if (flag)
				{
					result = 0;
				}
				else
				{
					TilePatternParams p = TileDimensionDetector.DetectPatternParams(element, doc, isFloor ? 300.0 : 300.0, isFloor ? 300.0 : 600.0, 3.0, 10.0);
					ElementId orCreateTileHatchMaterial = this.GetOrCreateTileHatchMaterial(doc, p);
					bool flag2 = orCreateTileHatchMaterial == ElementId.InvalidElementId;
					if (flag2)
					{
						result = 0;
					}
					else
					{
						bool flag3 = doc.IsPainted(hostId, face);
						if (flag3)
						{
							doc.RemovePaint(hostId, face);
						}
						doc.Paint(hostId, face, orCreateTileHatchMaterial);
						result = 1;
					}
				}
			}
			catch
			{
				result = 0;
			}
			return result;
		}
		private ElementId GetOrCreateTileHatchMaterial(Document doc, TilePatternParams p)
		{
			string str = string.Format("{0}_{1}x{2}_J{3:F1}", new object[]
			{
				(int)p.Style,
				(int)p.TileWidth,
				(int)p.TileHeight,
				p.JointWidth
			});
			string text = "TilePAT_" + str;
			string text2 = "TileMAT_" + str;
			FillPatternElement fillPatternElement = null;
			using (IEnumerator<Element> enumerator = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					FillPatternElement fillPatternElement2 = (FillPatternElement)enumerator.Current;
					bool flag = fillPatternElement2.Name.Equals(text, StringComparison.OrdinalIgnoreCase);
					if (flag)
					{
						fillPatternElement = fillPatternElement2;
						break;
					}
				}
			}
			bool flag2 = fillPatternElement == null;
			ElementId result;
			if (flag2)
			{
				FillPattern fillPattern = new FillPattern(text, FillPatternTarget.Model, FillPatternHostOrientation.ToHost);
				List<FillGrid> list = this.BuildFillGrids(p);
				bool flag3 = list.Count == 0;
				if (flag3)
				{
					result = ElementId.InvalidElementId;
					return result;
				}
				fillPattern.SetFillGrids(list);
				fillPatternElement = FillPatternElement.Create(doc, fillPattern);
			}
			Material material = null;
			using (IEnumerator<Element> enumerator2 = new FilteredElementCollector(doc).OfClass(typeof(Material)).GetEnumerator())
			{
				while (enumerator2.MoveNext())
				{
					Material material2 = (Material)enumerator2.Current;
					bool flag4 = material2.Name.Equals(text2, StringComparison.OrdinalIgnoreCase);
					if (flag4)
					{
						material = material2;
						break;
					}
				}
			}
			bool flag5 = material == null;
			if (flag5)
			{
				ElementId id = Material.Create(doc, text2);
				material = (doc.GetElement(id) as Material);
				bool flag6 = material != null && fillPatternElement != null;
				if (flag6)
				{
					material.Color = new Color(245, 245, 245);
					material.SurfaceForegroundPatternId = fillPatternElement.Id;
					material.SurfaceForegroundPatternColor = new Color(80, 80, 80);
				}
			}
			else
			{
				bool flag6 = material != null && fillPatternElement != null;
				if (flag6)
				{
					try
					{
						if (material.Color.Red != 245 || material.Color.Green != 245 || material.Color.Blue != 245)
						{
							material.Color = new Color(245, 245, 245);
						}
						if (material.SurfaceForegroundPatternId != fillPatternElement.Id)
						{
							material.SurfaceForegroundPatternId = fillPatternElement.Id;
						}
						if (material.SurfaceForegroundPatternColor.Red != 80 || material.SurfaceForegroundPatternColor.Green != 80 || material.SurfaceForegroundPatternColor.Blue != 80)
						{
							material.SurfaceForegroundPatternColor = new Color(80, 80, 80);
						}
					}
					catch { }
				}
			}
			result = (((material != null) ? material.Id : null) ?? ElementId.InvalidElementId);
			return result;
		}
		private List<FillGrid> BuildFillGrids(TilePatternParams p)
		{
			List<FillGrid> list = new List<FillGrid>();
			double u = p.TileWidth / 304.8;
			double num = p.TileHeight / 304.8;
			double num2 = p.JointWidth / 304.8;
			double offset = (p.TileWidth + p.JointWidth) / 304.8;
			double num3 = (p.TileHeight + p.JointWidth) / 304.8;
			if (p.JointWidth <= 0.001)
			{
				switch (p.Style)
				{
				case TilePatternStyle.RunningBond:
				case TilePatternStyle.StackOffset:
				{
					list.Add(new FillGrid
					{
						Angle = 0.0,
						Origin = UV.Zero,
						Offset = num
					});
					double shift = num * (p.OffsetPercent / 100.0);
					FillGrid fillGrid = new FillGrid
					{
						Angle = 1.5707963267948966,
						Origin = UV.Zero,
						Offset = u,
						Shift = shift
					};
					fillGrid.SetSegments(new List<double>
					{
						num,
						-num
					});
					list.Add(fillGrid);
					return list;
				}
				case TilePatternStyle.HorizontalJoint:
					list.Add(new FillGrid
					{
						Angle = 0.0,
						Origin = UV.Zero,
						Offset = num
					});
					return list;
				case TilePatternStyle.VerticalJoint:
					list.Add(new FillGrid
					{
						Angle = 1.5707963267948966,
						Origin = UV.Zero,
						Offset = u
					});
					return list;
				}
				list.Add(new FillGrid
				{
					Angle = 0.0,
					Origin = UV.Zero,
					Offset = num
				});
				list.Add(new FillGrid
				{
					Angle = 1.5707963267948966,
					Origin = UV.Zero,
					Offset = u
				});
				return list;
			}
			switch (p.Style)
			{
			case TilePatternStyle.RunningBond:
			case TilePatternStyle.StackOffset:
			{
				list.Add(new FillGrid
				{
					Angle = 0.0,
					Origin = UV.Zero,
					Offset = num3
				});
				list.Add(new FillGrid
				{
					Angle = 0.0,
					Origin = new UV(0.0, num),
					Offset = num3
				});
				double shift = num3 * (p.OffsetPercent / 100.0);
				FillGrid fillGrid = new FillGrid
				{
					Angle = 1.5707963267948966,
					Origin = UV.Zero,
					Offset = offset,
					Shift = shift
				};
				FillGrid fillGrid2 = new FillGrid
				{
					Angle = 1.5707963267948966,
					Origin = new UV(u, 0.0),
					Offset = offset,
					Shift = shift
				};
				fillGrid.SetSegments(new List<double>
				{
					num,
					-num2
				});
				fillGrid2.SetSegments(new List<double>
				{
					num,
					-num2
				});
				list.Add(fillGrid);
				list.Add(fillGrid2);
				return list;
			}
			case TilePatternStyle.HorizontalJoint:
				list.Add(new FillGrid
				{
					Angle = 0.0,
					Origin = UV.Zero,
					Offset = num3
				});
				list.Add(new FillGrid
				{
					Angle = 0.0,
					Origin = new UV(0.0, num),
					Offset = num3
				});
				return list;
			case TilePatternStyle.VerticalJoint:
				list.Add(new FillGrid
				{
					Angle = 1.5707963267948966,
					Origin = UV.Zero,
					Offset = offset
				});
				list.Add(new FillGrid
				{
					Angle = 1.5707963267948966,
					Origin = new UV(u, 0.0),
					Offset = offset
				});
				return list;
			}
			list.Add(new FillGrid
			{
				Angle = 0.0,
				Origin = UV.Zero,
				Offset = num3
			});
			list.Add(new FillGrid
			{
				Angle = 0.0,
				Origin = new UV(0.0, num),
				Offset = num3
			});
			list.Add(new FillGrid
			{
				Angle = 1.5707963267948966,
				Origin = UV.Zero,
				Offset = offset
			});
			list.Add(new FillGrid
			{
				Angle = 1.5707963267948966,
				Origin = new UV(u, 0.0),
				Offset = offset
			});
			return list;
		}
		private void EnsureJointSharedParamFile(string path)
		{
			bool flag = File.Exists(path);
			if (!flag)
			{
				using (StreamWriter streamWriter = new StreamWriter(path, false, Encoding.Unicode))
				{
					streamWriter.WriteLine("# This is a Revit shared parameter file.");
					streamWriter.WriteLine("# Do not edit manually.");
					streamWriter.WriteLine("*META\tVERSION\tMINVERSION");
					streamWriter.WriteLine("META\t2\t1");
					streamWriter.WriteLine("*GROUP\tID\tNAME");
					streamWriter.WriteLine("GROUP\t1\t磁磚系統");
					streamWriter.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE");
					streamWriter.WriteLine("PARAM\ta2b3c4d5-0001-4ebc-9999-ad1e0000001a\tTile_Joint_Width\tNUMBER\t\t1\t1\t磁磚縫隙寬度(mm)\t1");
				}
			}
		}
		private bool HasFinishLayer(Element elem)
		{
			CompoundStructure compoundStructure = null;
			Wall wall = elem as Wall;
			bool flag = wall != null;
			if (flag)
			{
				compoundStructure = wall.WallType.GetCompoundStructure();
			}
			else
			{
				Floor floor = elem as Floor;
				bool flag2 = floor != null;
				if (flag2)
				{
					compoundStructure = floor.FloorType.GetCompoundStructure();
				}
			}
			bool flag3 = compoundStructure == null;
			bool result;
			if (flag3)
			{
				result = false;
			}
			else
			{
				for (int i = 0; i < compoundStructure.LayerCount; i++)
				{
					MaterialFunctionAssignment layerFunction = compoundStructure.GetLayerFunction(i);
					bool flag4 = layerFunction == MaterialFunctionAssignment.Finish1 || layerFunction == MaterialFunctionAssignment.Finish2;
					if (flag4)
					{
						result = true;
						return result;
					}
				}
				result = false;
			}
			return result;
		}
		private int RemovePaintFromElement(Document doc, Element elem, HashSet<ElementId> matIds)
		{
			int num = 0;
			int result;
			try
			{
				Options options = new Options
				{
					DetailLevel = ViewDetailLevel.Fine,
					ComputeReferences = true
				};
				GeometryElement geometryElement = elem.get_Geometry(options);
				bool flag = geometryElement == null;
				if (flag)
				{
					result = 0;
					return result;
				}
				foreach (GeometryObject current in geometryElement)
				{
					Solid solid = current as Solid;
					bool flag2 = solid != null;
					if (flag2)
					{
						foreach (Face face in solid.Faces)
						{
							bool flag3 = doc.IsPainted(elem.Id, face);
							if (flag3)
							{
								ElementId paintedMaterial = doc.GetPaintedMaterial(elem.Id, face);
								bool flag4 = matIds.Contains(paintedMaterial);
								if (flag4)
								{
									doc.RemovePaint(elem.Id, face);
									num++;
								}
							}
						}
					}
				}
			}
			catch
			{
			}
			result = num;
			return result;
		}
		private XYZ GetRoomCenter(Room room)
		{
			LocationPoint locationPoint = room.Location as LocationPoint;
			bool flag = locationPoint != null;
			XYZ result;
			if (flag)
			{
				result = locationPoint.Point;
			}
			else
			{
				BoundingBoxXYZ boundingBoxXYZ = room.get_BoundingBox(null);
				result = ((boundingBoxXYZ != null) ? ((boundingBoxXYZ.Min + boundingBoxXYZ.Max) * 0.5) : XYZ.Zero);
			}
			return result;
		}
		private View3D FindAny3DView(Document doc)
		{
			View3D result;
			using (IEnumerator<Element> enumerator = new FilteredElementCollector(doc).OfClass(typeof(View3D)).GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					View3D view3D = (View3D)enumerator.Current;
					bool flag = !view3D.IsTemplate;
					if (flag)
					{
						result = view3D;
						return result;
					}
				}
			}
			result = null;
			return result;
		}
		private string CleanGroupName(string name)
		{
			string str = "\\\\:\\?\\{\\}\\[\\]\\|;<>\\`~";
			return Regex.Replace(name, "[" + str + "]", "_");
		}
		private void SetSharedParameter(Element elem, string paramName, object value)
		{
			Parameter parameter = elem.LookupParameter(paramName);
			bool flag = parameter == null || parameter.IsReadOnly;
			if (!flag)
			{
				string text = value as string;
				bool flag2 = text != null;
				if (flag2)
				{
					parameter.Set(text);
				}
				else
				{
					double value2 = 0.0;
					bool arg_4A_0;
					if (value is double)
					{
						value2 = (double)value;
						arg_4A_0 = true;
					}
					else
					{
						arg_4A_0 = false;
					}
					bool flag3 = arg_4A_0;
					if (flag3)
					{
						parameter.Set(value2);
					}
					else
					{
						int value3 = 0;
						bool arg_71_0;
						if (value is int)
						{
							value3 = (int)value;
							arg_71_0 = true;
						}
						else
						{
							arg_71_0 = false;
						}
						bool flag4 = arg_71_0;
						if (flag4)
						{
							parameter.Set(value3);
						}
					}
				}
			}
		}
		private void DoCreateSchedule(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("正在建立 Revit 磁磚明細表...");
				using (Transaction transaction = new Transaction(doc, "建立磁磚明細表"))
				{
					transaction.Start();
					var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
					HashSet<BuiltInCategory> activeCategories = new HashSet<BuiltInCategory>();
					foreach (Element elem in collector)
					{
						Parameter commentParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
						if (commentParam != null && commentParam.HasValue)
						{
							string val = commentParam.AsString();
							if (!string.IsNullOrEmpty(val) && (val.Contains("Tile_ID:") || val.StartsWith("Tile_")))
							{
								if (elem.Category != null)
								{
									BuiltInCategory bCat = (BuiltInCategory)elem.Category.Id.IntegerValue;
									activeCategories.Add(bCat);
								}
							}
						}
					}
					if (activeCategories.Count == 0)
					{
						activeCategories.Add(BuiltInCategory.OST_Parts);
						activeCategories.Add(BuiltInCategory.OST_Walls);
						activeCategories.Add(BuiltInCategory.OST_Floors);
					}
					ViewSchedule lastCreatedSchedule = null;
					List<ViewSchedule> createdSchedules = new List<ViewSchedule>();
					string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
					foreach (BuiltInCategory cat in activeCategories)
					{
						string catName = "其它";
						if (cat == BuiltInCategory.OST_Walls)
						{
							catName = "牆面";
						}
						else if (cat == BuiltInCategory.OST_Floors)
						{
							catName = "地面";
						}
						else if (cat == BuiltInCategory.OST_GenericModel)
						{
							catName = "一般模型";
						}
						else if (cat == BuiltInCategory.OST_Parts)
						{
							catName = "Parts";
						}
						else
						{
							Category categoryObject = Category.GetCategory(doc, cat);
							if (categoryObject != null)
							{
								catName = categoryObject.Name;
							}
						}
						ViewSchedule viewSchedule = ViewSchedule.CreateSchedule(doc, new ElementId(cat));
						viewSchedule.Name = $"磁磚統計明細表({catName})_{timestamp}";
						IList<SchedulableField> schedulableFields = viewSchedule.Definition.GetSchedulableFields();
						ScheduleField scheduleField = null;
						ScheduleField scheduleField2 = null;
						ScheduleField scheduleField3 = null;
						ScheduleField scheduleField4 = null;
						ScheduleField scheduleField5 = null;
						ScheduleField scheduleField6 = null;
						ScheduleField scheduleFieldSurface = null;
						foreach (SchedulableField current in schedulableFields)
						{
							if (current.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS))
							{
								try
								{
									scheduleField = viewSchedule.Definition.AddField(current);
									scheduleField.IsHidden = true;
								}
								catch
								{
								}
							}
							else
							{
								string name = current.GetName(doc);
								bool isCount = name.Equals("Count", StringComparison.OrdinalIgnoreCase) || name.Equals("數量", StringComparison.OrdinalIgnoreCase) || name.Equals("合計", StringComparison.OrdinalIgnoreCase);
								if (isCount)
								{
									try
									{
										ScheduleField scheduleField7 = viewSchedule.Definition.AddField(current);
										scheduleField7.ColumnHeading = "數量";
										scheduleField7.DisplayType = ScheduleFieldDisplayType.Totals;
									}
									catch
									{
									}
								}
								else if (name.Equals("Room_ID", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										scheduleField2 = viewSchedule.Definition.AddField(current);
										scheduleField2.ColumnHeading = "房間名稱";
									}
									catch
									{
									}
								}
								else if (name.Equals("Tile_Material", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										scheduleField3 = viewSchedule.Definition.AddField(current);
										scheduleField3.ColumnHeading = "磁磚材質";
									}
									catch
									{
									}
								}
								else if (name.Equals("Tile_Surface", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										scheduleFieldSurface = viewSchedule.Definition.AddField(current);
										scheduleFieldSurface.ColumnHeading = "位置";
									}
									catch
									{
									}
								}
								else if (name.Equals("Tile_Type", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										scheduleField4 = viewSchedule.Definition.AddField(current);
										scheduleField4.ColumnHeading = "磁磚類型";
									}
									catch
									{
									}
								}
								else if (name.Equals("Tile_Width", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										scheduleField5 = viewSchedule.Definition.AddField(current);
										scheduleField5.ColumnHeading = "規格寬(mm)";
									}
									catch
									{
									}
								}
								else if (name.Equals("Tile_Height", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										scheduleField6 = viewSchedule.Definition.AddField(current);
										scheduleField6.ColumnHeading = "規格高(mm)";
									}
									catch
									{
									}
								}
								else if (name.Equals("Tile_Size", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										ScheduleField tileSizeField = viewSchedule.Definition.AddField(current);
										tileSizeField.ColumnHeading = "規格";
									}
									catch
									{
									}
								}
								else if (name.Equals("Tile_Thickness", StringComparison.OrdinalIgnoreCase))
								{
									try
									{
										ScheduleField scheduleField8 = viewSchedule.Definition.AddField(current);
										scheduleField8.ColumnHeading = "規格厚(mm)";
									}
									catch
									{
									}
								}
							}
						}
						viewSchedule.Definition.IsItemized = false;
						viewSchedule.Definition.ShowGrandTotal = true;
						if (scheduleFieldSurface != null)
						{
							ScheduleSortGroupField surfaceGroupField = new ScheduleSortGroupField(scheduleFieldSurface.FieldId);
							surfaceGroupField.ShowHeader = true;
							surfaceGroupField.ShowFooter = true;
							this.TryAddScheduleSortGroupField(viewSchedule, surfaceGroupField);
						}
						if (scheduleField2 != null)
						{
							ScheduleSortGroupField scheduleSortGroupField = new ScheduleSortGroupField(scheduleField2.FieldId);
							scheduleSortGroupField.ShowHeader = true;
							scheduleSortGroupField.ShowFooter = true;
							this.TryAddScheduleSortGroupField(viewSchedule, scheduleSortGroupField);
						}
						if (scheduleField3 != null)
						{
							ScheduleSortGroupField sortGroupField = new ScheduleSortGroupField(scheduleField3.FieldId);
							this.TryAddScheduleSortGroupField(viewSchedule, sortGroupField);
						}
						if (scheduleField4 != null)
						{
							ScheduleSortGroupField sortGroupField2 = new ScheduleSortGroupField(scheduleField4.FieldId);
							this.TryAddScheduleSortGroupField(viewSchedule, sortGroupField2);
						}
						if (scheduleField5 != null)
						{
							ScheduleSortGroupField sortGroupField3 = new ScheduleSortGroupField(scheduleField5.FieldId);
							this.TryAddScheduleSortGroupField(viewSchedule, sortGroupField3);
						}
						if (scheduleField6 != null)
						{
							ScheduleSortGroupField sortGroupField4 = new ScheduleSortGroupField(scheduleField6.FieldId);
							this.TryAddScheduleSortGroupField(viewSchedule, sortGroupField4);
						}
						if (scheduleField != null)
						{
							ScheduleFilter filter = new ScheduleFilter(scheduleField.FieldId, ScheduleFilterType.Contains, "Tile_ID:");
							viewSchedule.Definition.AddFilter(filter);
						}
						lastCreatedSchedule = viewSchedule;
						createdSchedules.Add(viewSchedule);
					}
					ViewSheet activeSheet = uidoc.ActiveView as ViewSheet;
					if (activeSheet != null)
					{
						double y = 0.85;
						foreach (ViewSchedule schedule in createdSchedules)
						{
							try
							{
								ScheduleSheetInstance.Create(doc, activeSheet.Id, schedule.Id, new XYZ(0.08, y, 0.0));
								y -= 0.18;
							}
							catch { }
						}
					}
					transaction.Commit();
					if (lastCreatedSchedule != null && activeSheet == null)
					{
						uidoc.ActiveView = lastCreatedSchedule;
					}
				}
				this.ViewModel.SetStatus(uidoc.ActiveView is ViewSheet
					? "建立 Revit 磁磚明細表完成，已放置到目前圖紙。"
					: "建立 Revit 磁磚明細表完成。");
			}
			catch (Exception ex)
			{
				TaskDialog.Show("建立明細表錯誤", ex.ToString());
				this.ViewModel.SetStatus("建立明細表失敗: " + ex.Message);
			}
		}

		private void DoEstimateSelectedWallFaces(UIDocument uidoc, Document doc)
		{
			try
			{
				List<Tuple<string, double, TilePatternParams, string>> items = new List<Tuple<string, double, TilePatternParams, string>>();
				List<Wall> selectedWalls = uidoc.Selection.GetElementIds()
					.Select(id => doc.GetElement(id))
					.OfType<Wall>()
					.Where(wall => wall != null && wall.IsValidObject)
					.GroupBy(wall => wall.Id.IntegerValue)
					.Select(group => group.First())
					.ToList();

				foreach (Wall wall in selectedWalls)
				{
					double areaSqm = this.GetElementAreaSquareMeters(wall);
					if (areaSqm <= 0.0001) continue;
					TilePatternParams p = TileDimensionDetector.DetectPatternParams(wall, doc, this.ViewModel.TileWidth, this.ViewModel.TileHeight, this.ViewModel.JointWidth, this.ViewModel.TileThickness);
					ElementId materialId = this.GetHostFinishMaterial(wall);
					Material material = materialId == ElementId.InvalidElementId ? null : doc.GetElement(materialId) as Material;
					if (material != null)
					{
						TilePatternParams fromMaterial = this.ParseParamsFromMatName(material.Name);
						if (fromMaterial != null)
						{
							p.TileWidth = fromMaterial.TileWidth;
							p.TileHeight = fromMaterial.TileHeight;
							p.JointWidth = fromMaterial.JointWidth;
						}
						else if (TileDimensionDetector.ParseDimensions(material.Name, out double w, out double h))
						{
							p.TileWidth = w;
							p.TileHeight = h;
						}
					}
					items.Add(Tuple.Create(wall.Name + " #" + wall.Id.IntegerValue, areaSqm, p, material == null ? "未指定材質" : material.Name));
				}

				if (items.Count == 0)
				{
					IList<Reference> picked;
					try
					{
						picked = uidoc.Selection.PickObjects(ObjectType.Face, "請選取要粗估磁磚數量的牆面。");
					}
					catch (Autodesk.Revit.Exceptions.OperationCanceledException)
					{
						return;
					}

					foreach (Reference reference in picked ?? new List<Reference>())
					{
						Element pickedElement = doc.GetElement(reference.ElementId);
						Element hostElement = this.ResolvePatOnlyPartsHost(doc, pickedElement);
						if (!(hostElement is Wall)) continue;
						PlanarFace face = pickedElement == null ? null : pickedElement.GetGeometryObjectFromReference(reference) as PlanarFace;
						if (face == null) continue;
						TilePatternParams p = this.ResolveSelectedTilePatternParams(doc, hostElement, face);
						Material material = this.GetSelectedFaceMaterial(doc, hostElement, face);
						items.Add(Tuple.Create(hostElement.Name + " #" + hostElement.Id.IntegerValue, face.Area * 0.09290304, p, material == null ? "未指定材質" : material.Name));
					}
				}

				if (items.Count == 0)
				{
					TaskDialog.Show("磁磚粗估", "沒有取得可計算的牆面。請先選取牆，或點選牆面 Face。");
					return;
				}

				double totalArea = 0.0;
				double totalTiles = 0.0;
				StringBuilder sb = new StringBuilder();
				sb.AppendLine("選取牆面磁磚粗估");
				sb.AppendLine();
				foreach (var item in items)
				{
					TilePatternParams p = item.Item3;
					double tileAreaSqm = Math.Max(0.000001, p.TileWidth * p.TileHeight / 1000000.0);
					double count = Math.Ceiling(item.Item2 / tileAreaSqm);
					totalArea += item.Item2;
					totalTiles += count;
					sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}：{1:0.###} m2，{2:0.#}x{3:0.#} mm，約 {4:0} 片，材質 {5}", item.Item1, item.Item2, p.TileWidth, p.TileHeight, count, item.Item4));
				}
				sb.AppendLine();
				sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "合計面積：{0:0.###} m2", totalArea));
				sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "粗估片數：{0:0} 片", totalTiles));
				TaskDialog.Show("磁磚粗估", sb.ToString());
				this.ViewModel.SetStatus(string.Format(CultureInfo.InvariantCulture, "選取牆面粗估完成：{0} 面/道，合計 {1:0.###} m2，約 {2:0} 片。", items.Count, totalArea, totalTiles));
			}
			catch (Exception ex)
			{
				TaskDialog.Show("磁磚粗估錯誤", ex.ToString());
				this.ViewModel.SetStatus("選取牆面粗估失敗: " + ex.Message);
			}
		}

		private double GetElementAreaSquareMeters(Element element)
		{
			if (element == null) return 0.0;
			try
			{
				Parameter areaParam = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
				if (areaParam != null && areaParam.HasValue) return areaParam.AsDouble() * 0.09290304;
			}
			catch { }
			return 0.0;
		}

		private void DoCreateCurrentSpaceTileSchedule(UIDocument uidoc, Document doc)
		{
			try
			{
				List<string> selectedHostKeys = this.GetSelectedTileHostKeys(doc, uidoc.Selection.GetElementIds());
				if (selectedHostKeys.Count > 0)
				{
					string selectionLabel = this.BuildTileSelectionLabel(selectedHostKeys);
					ViewSchedule existingCheckSchedule = this.FindExistingTileCheckSchedule(doc, selectionLabel);
					if (existingCheckSchedule != null)
					{
						uidoc.ActiveView = existingCheckSchedule;
						this.ViewModel.SetStatus(string.Format("已開啟既有磁磚檢核表：{0}。", existingCheckSchedule.Name));
						return;
					}
					List<Part> selectedParts = new List<Part>();
					using (Transaction transaction = new Transaction(doc, "建立選取天地牆磁磚表"))
					{
						transaction.Start();
						SharedParameterHelper.RegisterAndBindParameters(doc);
						selectedParts = this.UpdateAndCollectTilePartsForSelectedHosts(doc, selectedHostKeys, selectionLabel);
						if (selectedParts.Count == 0)
						{
							transaction.RollBack();
							TaskDialog.Show("Tile Parts", "目前選取的天地牆沒有找到已標記的磁磚 Parts。請先完成分割，或重新執行 Step 3 標記 Parts。");
							return;
						}

						_currentTileLayoutSelectionLabel = selectionLabel;
						_currentTileLayoutSelectionPartIds.Clear();
						_currentTileLayoutSelectionPartIds.AddRange(selectedParts.Select(part => part.Id));
						_currentTileLayoutRoomId = ElementId.InvalidElementId;
						_currentTileLayoutSpaceLabel = selectionLabel;

						ViewSchedule checkSchedule = this.CreateSelectedTilePartsSchedule(doc, selectionLabel);
						ViewSchedule purchaseSchedule = this.CreateSelectedTilePurchaseSchedule(doc, selectionLabel);
						ViewSheet activeSheet = uidoc.ActiveView as ViewSheet;
						if (activeSheet != null)
						{
							this.PlaceScheduleOnSheetIfMissing(doc, activeSheet, checkSchedule, new XYZ(0.08, 0.85, 0.0));
							this.PlaceScheduleOnSheetIfMissing(doc, activeSheet, purchaseSchedule, new XYZ(0.08, 0.55, 0.0));
						}
						transaction.Commit();

						if (checkSchedule != null)
						{
							uidoc.ActiveView = checkSchedule;
						}
					}

					this.ViewModel.SetStatus(string.Format("已建立選取天地牆磁磚檢核表與採購表：{0}，共 {1} 個 Parts。", selectionLabel, selectedParts.Count));
					return;
				}

				Reference roomReference;
				try
				{
					roomReference = uidoc.Selection.PickObject(ObjectType.Element, new RoomSelectionFilter(), "請選取要建立磁磚表的房間。");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					return;
				}

				Room room = roomReference == null ? null : doc.GetElement(roomReference.ElementId) as Room;
				if (room == null)
				{
					TaskDialog.Show("Tile Parts", "沒有選到有效房間，無法建立目前空間磁磚表。");
					return;
				}

				string roomLabel = this.GetRoomScheduleLabel(room);
				_currentTileLayoutSpaceLabel = roomLabel;
				_currentTileLayoutRoomId = room.Id;
				_currentTileLayoutSelectionLabel = string.Empty;
				_currentTileLayoutSelectionPartIds.Clear();
				ViewSchedule existingRoomCheckSchedule = this.FindExistingTileCheckSchedule(doc, roomLabel);
				if (existingRoomCheckSchedule != null)
				{
					uidoc.ActiveView = existingRoomCheckSchedule;
					this.ViewModel.SetStatus(string.Format("已開啟既有空間磁磚檢核表：{0}。", existingRoomCheckSchedule.Name));
					return;
				}
				List<Part> matchingParts = new List<Part>();
				using (Transaction transaction = new Transaction(doc, "建立目前空間磁磚表"))
				{
					transaction.Start();
					SharedParameterHelper.RegisterAndBindParameters(doc);
					matchingParts = this.UpdateAndCollectTilePartsForRoom(doc, room, roomLabel);
					if (matchingParts.Count == 0)
					{
						transaction.RollBack();
						TaskDialog.Show("Tile Parts", "這個空間目前沒有找到已分割的磁磚 Parts。請先完成分割，或確認 Parts 位於該房間內。");
						return;
					}

					ViewSchedule checkSchedule = this.CreateRoomTilePartsSchedule(doc, roomLabel);
					ViewSchedule purchaseSchedule = this.CreateRoomTilePurchaseSchedule(doc, roomLabel);
					ViewSheet activeSheet = uidoc.ActiveView as ViewSheet;
					if (activeSheet != null)
					{
						this.PlaceScheduleOnSheetIfMissing(doc, activeSheet, checkSchedule, new XYZ(0.08, 0.85, 0.0));
						this.PlaceScheduleOnSheetIfMissing(doc, activeSheet, purchaseSchedule, new XYZ(0.08, 0.55, 0.0));
					}
					transaction.Commit();

					if (checkSchedule != null)
					{
						uidoc.ActiveView = checkSchedule;
					}
				}

				this.ViewModel.SetStatus(string.Format("已建立目前空間磁磚檢核表與採購表：{0}，共 {1} 個 Parts。", roomLabel, matchingParts.Count));
			}
			catch (Exception ex)
			{
				TaskDialog.Show("目前空間磁磚表錯誤", ex.ToString());
				this.ViewModel.SetStatus("建立目前空間磁磚表失敗: " + ex.Message);
			}
		}

		private void DoOpenTilePurchaseDashboard(UIDocument uidoc, Document doc)
		{
			try
			{
				List<TilePurchaseDashboardItem> items = this.BuildTilePurchaseDashboardItems(doc);
				if (items.Count == 0)
				{
					TaskDialog.Show("Tile Parts", "目前專案沒有可彙總的磁磚 Parts。請先完成建立零件與分割零件。");
					return;
				}

				TilePurchaseDashboardWindow window = new TilePurchaseDashboardWindow(items);
				window.Show();
				this.ViewModel.SetStatus(string.Format("已開啟磁磚採購/檢查介面：{0} 筆介面彙總。", items.Count));
			}
			catch (Exception ex)
			{
				TaskDialog.Show("磁磚採購/檢查介面錯誤", ex.ToString());
				this.ViewModel.SetStatus("磁磚採購/檢查介面失敗: " + ex.Message);
			}
		}

		private List<TilePurchaseDashboardItem> BuildTilePurchaseDashboardItems(Document doc)
		{
			List<Part> tileParts = new FilteredElementCollector(doc)
				.OfClass(typeof(Part))
				.WhereElementIsNotElementType()
				.OfType<Part>()
				.Where(part =>
				{
					if (part == null || this.IsPartExcludedByMaker(part)) return false;
					Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
					string comments = commentParam == null ? null : commentParam.AsString();
					return !string.IsNullOrWhiteSpace(comments) && comments.Contains("Tile_ID:");
				})
				.ToList();

			List<TilePurchaseDashboardItem> items = new List<TilePurchaseDashboardItem>();
			foreach (IGrouping<string, Part> group in tileParts.GroupBy(part => this.GetTileSurfaceAreaGroupKey(part)))
			{
				List<Part> groupParts = group.ToList();
				if (groupParts.Count == 0) continue;
				Part first = groupParts[0];
				Parameter commentParam = first.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string comments = commentParam == null ? null : commentParam.AsString();

				string room = first.LookupParameter("Room_ID")?.AsString();
				if (string.IsNullOrWhiteSpace(room)) room = this.ParseCommentValue(comments, "Room_ID");
				if (string.IsNullOrWhiteSpace(room)) room = "未指定空間";

				string surface = first.LookupParameter("Tile_Surface")?.AsString();
				if (string.IsNullOrWhiteSpace(surface)) surface = this.ParseCommentValue(comments, "Surface");
				if (string.IsNullOrWhiteSpace(surface)) surface = this.GetTileSurfaceLabelFromPart(first);

				string material = first.LookupParameter("Tile_Material")?.AsString();
				if (string.IsNullOrWhiteSpace(material)) material = "未指定材質";

				string size = first.LookupParameter("Tile_Size")?.AsString();
				if (string.IsNullOrWhiteSpace(size)) size = this.ParseCommentValue(comments, "Size");
				if (string.IsNullOrWhiteSpace(size)) size = this.GetTileSizeLabelFromPart(first);

				TileWasteSummary summary = this.CalculateTileWasteSummary(groupParts);
				double areaSqm = this.GetTileGroupSurfaceAreaSquareMeters(groupParts);
				bool checkExists = this.TileScheduleExists(doc, room, "檢核");
				bool purchaseExists = this.TileScheduleExists(doc, room, "採購");

				items.Add(new TilePurchaseDashboardItem
				{
					Room = room,
					Surface = surface,
					Material = material,
					Size = size,
					SurfaceAreaText = areaSqm.ToString("0.###", CultureInfo.InvariantCulture),
					NetQtyText = this.FormatTileQuantity(summary.NetQty),
					PurchaseQtyText = this.FormatTileQuantity(summary.PurchaseQty),
					WasteText = string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.##}%)", this.FormatTileQuantity(summary.WasteQty), summary.WasteRate),
					CheckScheduleStatus = checkExists ? "已建立" : "未建立",
					PurchaseScheduleStatus = purchaseExists ? "已建立" : "未建立",
					CalculationMethod = this.GetPurchaseCalculationMethod(summary),
					PartCount = groupParts.Count
				});
			}

			return items
				.OrderBy(item => item.Room, StringComparer.OrdinalIgnoreCase)
				.ThenBy(item => item.Surface, StringComparer.OrdinalIgnoreCase)
				.ThenBy(item => item.Material, StringComparer.OrdinalIgnoreCase)
				.ThenBy(item => item.Size, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private void DoHighlightTilePartsByType(UIDocument uidoc, Document doc, string tileType, string displayName)
		{
			try
			{
				string selectionScopeLabel;
				List<ElementId> selectionScopeIds = this.GetTilePartIdsForActiveSelectionScope(doc, uidoc.ActiveView, uidoc.Selection.GetElementIds(), tileType, out selectionScopeLabel);
				if (selectionScopeIds.Count > 0)
				{
					this.ShowTileCheckParts3D(uidoc, doc, selectionScopeIds, selectionScopeLabel, displayName);
					this.ViewModel.SetStatus(string.Format("Tile check [{0}] {1}: {2} Parts. 3D view is cropped to this scope.", selectionScopeLabel, displayName, selectionScopeIds.Count));
					return;
				}

				Room room = _currentTileLayoutRoomId == ElementId.InvalidElementId ? null : doc.GetElement(_currentTileLayoutRoomId) as Room;
				if (room == null)
				{
					try
					{
						Reference roomReference = uidoc.Selection.PickObject(ObjectType.Element, new RoomSelectionFilter(), "請選取要檢核磁磚數量的房間。");
						room = roomReference == null ? null : doc.GetElement(roomReference.ElementId) as Room;
					}
					catch (Autodesk.Revit.Exceptions.OperationCanceledException)
					{
						return;
					}
				}

				if (room == null)
				{
					TaskDialog.Show("Tile Parts", "沒有選到有效房間，無法檢核磁磚 Parts。");
					return;
				}

				string roomLabel = this.GetRoomScheduleLabel(room);
				_currentTileLayoutSpaceLabel = roomLabel;
				_currentTileLayoutRoomId = room.Id;

				List<ElementId> ids = this.GetTilePartIdsForRoomAndType(doc, roomLabel, tileType);
				if (ids.Count == 0)
				{
					TaskDialog.Show("Tile Parts", string.Format("目前空間 [{0}] 沒有找到類型 [{1}] 的磁磚 Parts。請先重新執行 Step 3 並建立目前空間磁磚表。", roomLabel, tileType));
					return;
				}

				this.ShowTileCheckParts3D(uidoc, doc, ids, roomLabel, displayName);
				this.ViewModel.SetStatus(string.Format("已亮顯 [{0}] 的 {1}：{2} 片。表格中 {3} 的 Count 應與此數一致。", roomLabel, displayName, ids.Count, tileType));
			}
			catch (Exception ex)
			{
				TaskDialog.Show("磁磚數量檢核錯誤", ex.ToString());
				this.ViewModel.SetStatus("磁磚數量檢核失敗: " + ex.Message);
			}
		}

		private List<ElementId> GetTilePartIdsForActiveSelectionScope(Document doc, View activeView, ICollection<ElementId> selectedIds, string tileType, out string scopeLabel)
		{
			scopeLabel = string.Empty;
			List<string> selectedHostKeys = this.GetSelectedTileHostKeys(doc, selectedIds);
			if (selectedHostKeys.Count > 0)
			{
				List<ElementId> selectedIdsForType = this.GetTilePartIdsForHostKeysAndType(doc, selectedHostKeys, tileType);
				if (selectedIdsForType.Count > 0)
				{
					scopeLabel = "Current Selection";
					return selectedIdsForType;
				}
			}

			List<ElementId> activeScheduleIds = this.GetTilePartIdsForScheduleViewAndType(doc, activeView as ViewSchedule, tileType, out scopeLabel);
			if (activeScheduleIds.Count > 0) return activeScheduleIds;

			if (!string.IsNullOrWhiteSpace(_currentTileLayoutSelectionLabel))
			{
				List<ElementId> ids = this.GetTilePartIdsForSelectionSetAndType(doc, _currentTileLayoutSelectionLabel, tileType);
				if (ids.Count > 0)
				{
					scopeLabel = _currentTileLayoutSelectionLabel;
					return ids;
				}
			}

			if (_currentTileLayoutSelectionPartIds.Count > 0)
			{
				List<ElementId> ids = _currentTileLayoutSelectionPartIds
					.Select(id => doc.GetElement(id) as Part)
					.Where(part => part != null && part.IsValidObject && this.PartHasTileType(part, tileType))
					.Select(part => part.Id)
					.ToList();
				if (ids.Count > 0)
				{
					scopeLabel = "Last Selection";
					return ids;
				}
			}

			return new List<ElementId>();
		}

		private List<ElementId> GetTilePartIdsForHostKeysAndType(Document doc, IList<string> hostKeys, string tileType)
		{
			HashSet<string> keys = new HashSet<string>(hostKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			List<ElementId> ids = new List<ElementId>();
			if (keys.Count == 0) return ids;

			foreach (Part part in new FilteredElementCollector(doc)
				.OfClass(typeof(Part))
				.WhereElementIsNotElementType()
				.OfType<Part>())
			{
				if (part == null || this.IsPartExcludedByMaker(part) || !this.PartHasTileType(part, tileType)) continue;

				Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string comments = commentParam == null ? null : commentParam.AsString();
				if (string.IsNullOrEmpty(comments) || !comments.Contains("Tile_ID:")) continue;

				string anchorId = this.ParseCommentValue(comments, "Anchor_ID");
				string hostId = this.ParseCommentValue(comments, "Host_ID");
				if (keys.Contains(part.Id.IntegerValue.ToString()) ||
					(!string.IsNullOrWhiteSpace(anchorId) && keys.Contains(anchorId)) ||
					(!string.IsNullOrWhiteSpace(hostId) && keys.Contains(hostId)))
				{
					ids.Add(part.Id);
				}
			}

			return ids;
		}

		private List<ElementId> GetTilePartIdsForScheduleViewAndType(Document doc, ViewSchedule schedule, string tileType, out string scopeLabel)
		{
			scopeLabel = string.Empty;
			List<ElementId> ids = new List<ElementId>();
			if (doc == null || schedule == null || string.IsNullOrWhiteSpace(schedule.Name)) return ids;

			List<Part> tileParts = new FilteredElementCollector(doc)
				.OfClass(typeof(Part))
				.WhereElementIsNotElementType()
				.OfType<Part>()
				.Where(part => part != null && !this.IsPartExcludedByMaker(part) && this.PartHasTileType(part, tileType))
				.ToList();

			foreach (string selectionLabel in tileParts
				.Select(part => part.LookupParameter("Tile_SelectionSet")?.AsString())
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (!schedule.Name.Contains(selectionLabel)) continue;
				scopeLabel = selectionLabel;
				return tileParts
					.Where(part => string.Equals(part.LookupParameter("Tile_SelectionSet")?.AsString(), selectionLabel, StringComparison.OrdinalIgnoreCase))
					.Select(part => part.Id)
					.ToList();
			}

			foreach (string roomLabel in tileParts
				.Select(part => part.LookupParameter("Room_ID")?.AsString())
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (!schedule.Name.Contains(roomLabel)) continue;
				scopeLabel = roomLabel;
				return tileParts
					.Where(part => string.Equals(part.LookupParameter("Room_ID")?.AsString(), roomLabel, StringComparison.OrdinalIgnoreCase))
					.Select(part => part.Id)
					.ToList();
			}

			return ids;
		}

		private List<ElementId> GetTilePartIdsForSelectionSetAndType(Document doc, string selectionLabel, string tileType)
		{
			List<ElementId> ids = new List<ElementId>();
			if (string.IsNullOrWhiteSpace(selectionLabel)) return ids;

			foreach (Part part in new FilteredElementCollector(doc)
				.OfClass(typeof(Part))
				.WhereElementIsNotElementType()
				.OfType<Part>())
			{
				if (part == null || this.IsPartExcludedByMaker(part) || !this.PartHasTileType(part, tileType)) continue;
				string value = part.LookupParameter("Tile_SelectionSet")?.AsString();
				if (string.Equals(value, selectionLabel, StringComparison.OrdinalIgnoreCase))
				{
					ids.Add(part.Id);
				}
			}

			return ids;
		}

		private bool PartHasTileType(Part part, string tileType)
		{
			if (part == null) return false;
			Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			if (string.IsNullOrEmpty(comments) || !comments.Contains("Tile_ID:")) return false;

			string partType = part.LookupParameter("Tile_Type")?.AsString();
			if (string.IsNullOrWhiteSpace(partType)) partType = this.ParseCommentValue(comments, "Type");
			return string.Equals(partType, tileType, StringComparison.OrdinalIgnoreCase);
		}

		private void ShowTileCheckParts3D(UIDocument uidoc, Document doc, IList<ElementId> ids, string scopeLabel, string displayName)
		{
			if (uidoc == null || doc == null || ids == null || ids.Count == 0) return;

			View3D checkView = null;
			using (Transaction transaction = new Transaction(doc, "Tile check 3D section box"))
			{
				transaction.Start();
				checkView = this.GetOrCreateTileCheck3DView(doc);
				if (checkView != null)
				{
					this.EnablePatOnlyPartsViews(doc, checkView);
					BoundingBoxXYZ sectionBox = this.GetExpandedElementsBoundingBox(doc, ids, 1.0);
					if (sectionBox != null)
					{
						try
						{
							checkView.IsSectionBoxActive = true;
							checkView.SetSectionBox(sectionBox);
						}
						catch { }
					}
				}
				transaction.Commit();
			}

			if (checkView != null)
			{
				uidoc.ActiveView = checkView;
			}
			uidoc.Selection.SetElementIds(ids.ToList());
			try { uidoc.ShowElements(ids); } catch { }
			uidoc.RefreshActiveView();
		}

		private View3D GetOrCreateTileCheck3DView(Document doc)
		{
			View3D existing = new FilteredElementCollector(doc)
				.OfClass(typeof(View3D))
				.Cast<View3D>()
				.FirstOrDefault(view => view != null && !view.IsTemplate && string.Equals(view.Name, "Tile_Check_3D", StringComparison.OrdinalIgnoreCase));
			if (existing != null) return existing;

			ViewFamilyType viewFamilyType = new FilteredElementCollector(doc)
				.OfClass(typeof(ViewFamilyType))
				.Cast<ViewFamilyType>()
				.FirstOrDefault(type => type.ViewFamily == ViewFamily.ThreeDimensional);
			if (viewFamilyType == null) return this.FindAny3DView(doc);

			View3D view3D = View3D.CreateIsometric(doc, viewFamilyType.Id);
			try { view3D.Name = "Tile_Check_3D"; } catch { }
			return view3D;
		}

		private BoundingBoxXYZ GetExpandedElementsBoundingBox(Document doc, IList<ElementId> ids, double paddingFeet)
		{
			XYZ min = null;
			XYZ max = null;
			foreach (ElementId id in ids ?? new List<ElementId>())
			{
				Element element = doc.GetElement(id);
				BoundingBoxXYZ box = element == null ? null : element.get_BoundingBox(null);
				if (box == null) continue;

				min = min == null
					? box.Min
					: new XYZ(Math.Min(min.X, box.Min.X), Math.Min(min.Y, box.Min.Y), Math.Min(min.Z, box.Min.Z));
				max = max == null
					? box.Max
					: new XYZ(Math.Max(max.X, box.Max.X), Math.Max(max.Y, box.Max.Y), Math.Max(max.Z, box.Max.Z));
			}

			if (min == null || max == null) return null;
			double pad = Math.Max(0.1, paddingFeet);
			XYZ padding = new XYZ(pad, pad, pad);
			BoundingBoxXYZ result = new BoundingBoxXYZ();
			result.Min = min - padding;
			result.Max = max + padding;
			return result;
		}

		private List<ElementId> GetTilePartIdsForRoomAndType(Document doc, string roomLabel, string tileType)
		{
			List<ElementId> ids = new List<ElementId>();
			foreach (Part part in new FilteredElementCollector(doc)
				.OfClass(typeof(Part))
				.WhereElementIsNotElementType()
				.OfType<Part>())
			{
				if (part == null || this.IsPartExcludedByMaker(part)) continue;

				Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string comments = commentParam == null ? null : commentParam.AsString();
				if (string.IsNullOrEmpty(comments) || !comments.Contains("Tile_ID:")) continue;

				string partRoom = part.LookupParameter("Room_ID")?.AsString();
				if (string.IsNullOrWhiteSpace(partRoom)) partRoom = this.ParseCommentValue(comments, "Room_ID");
				if (!string.Equals(partRoom, roomLabel, StringComparison.OrdinalIgnoreCase)) continue;

				string partType = part.LookupParameter("Tile_Type")?.AsString();
				if (string.IsNullOrWhiteSpace(partType)) partType = this.ParseCommentValue(comments, "Type");
				if (!string.Equals(partType, tileType, StringComparison.OrdinalIgnoreCase)) continue;

				ids.Add(part.Id);
			}
			return ids;
		}

		private List<string> GetSelectedTileHostKeys(Document doc, ICollection<ElementId> selectedIds)
		{
			HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (doc == null || selectedIds == null) return keys.ToList();

			foreach (ElementId id in selectedIds)
			{
				Element element = doc.GetElement(id);
				if (element == null) continue;

				if (this.IsTileHostCandidate(element))
				{
					keys.Add(element.Id.IntegerValue.ToString());
					continue;
				}

				Part part = element as Part;
				if (part != null)
				{
					keys.Add(part.Id.IntegerValue.ToString());
					Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
					string comments = commentParam == null ? null : commentParam.AsString();
					string anchorId = this.ParseCommentValue(comments, "Anchor_ID");
					string hostId = this.ParseCommentValue(comments, "Host_ID");
					if (!string.IsNullOrWhiteSpace(anchorId)) keys.Add(anchorId);
					if (!string.IsNullOrWhiteSpace(hostId)) keys.Add(hostId);
				}
			}

			return keys.ToList();
		}

		private bool IsTileHostCandidate(Element element)
		{
			if (element == null) return false;
			if (element is Wall || element is Floor || element is Ceiling || element is RoofBase) return true;
			if (element.Category == null) return false;
			int categoryId = element.Category.Id.IntegerValue;
			return categoryId == (int)BuiltInCategory.OST_Walls ||
				categoryId == (int)BuiltInCategory.OST_Floors ||
				categoryId == (int)BuiltInCategory.OST_Ceilings ||
				categoryId == (int)BuiltInCategory.OST_Roofs;
		}

		private List<Part> UpdateAndCollectTilePartsForSelectedHosts(Document doc, IList<string> selectedHostKeys, string selectionLabel)
		{
			HashSet<string> hostKeys = new HashSet<string>(selectedHostKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			List<Part> result = new List<Part>();
			if (doc == null || hostKeys.Count == 0) return result;

			foreach (Part part in new FilteredElementCollector(doc)
				.OfClass(typeof(Part))
				.WhereElementIsNotElementType()
				.OfType<Part>())
			{
				if (part == null || this.IsPartExcludedByMaker(part)) continue;

				Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string comments = commentParam == null ? null : commentParam.AsString();
				if (string.IsNullOrEmpty(comments) || !comments.Contains("Tile_ID:")) continue;

				string anchorId = this.ParseCommentValue(comments, "Anchor_ID");
				string hostId = this.ParseCommentValue(comments, "Host_ID");
				bool matches = hostKeys.Contains(part.Id.IntegerValue.ToString()) ||
					(!string.IsNullOrWhiteSpace(anchorId) && hostKeys.Contains(anchorId)) ||
					(!string.IsNullOrWhiteSpace(hostId) && hostKeys.Contains(hostId));
				if (!matches) continue;

				if (!string.IsNullOrWhiteSpace(anchorId))
				{
					this.SetSharedParameter(part, "Anchor_ID", anchorId);
				}
				else if (!string.IsNullOrWhiteSpace(hostId))
				{
					this.SetSharedParameter(part, "Anchor_ID", hostId);
				}
				this.SetSharedParameter(part, "Tile_SelectionSet", selectionLabel);
				this.NormalizeTileScheduleFieldsForPart(part);
				this.SetTileWasteParameters(part);
				result.Add(part);
			}

			this.ApplyAutomaticWasteRatesForParts(result);
			return result;
		}

		private ViewSchedule CreateSelectedTilePartsSchedule(Document doc, string selectionLabel)
		{
			string scheduleName = "磁磚檢核表_" + this.SanitizeScheduleName(selectionLabel);
			ViewSchedule existing = this.FindTileSchedule(doc, scheduleName, this.SanitizeScheduleName(selectionLabel), "檢核");
			if (existing != null) return existing;

			ViewSchedule schedule = this.CreateBaseTilePartsSchedule(doc, scheduleName);
			IList<SchedulableField> fields = schedule.Definition.GetSchedulableFields();
			ScheduleField selectionField = this.TryAddScheduleField(doc, schedule, fields, "Tile_SelectionSet", "選取範圍", true);
			if (selectionField != null)
			{
				schedule.Definition.AddFilter(new ScheduleFilter(selectionField.FieldId, ScheduleFilterType.Equal, selectionLabel));
			}
			return schedule;
		}

		private ViewSchedule CreateSelectedTilePurchaseSchedule(Document doc, string selectionLabel)
		{
			string scheduleName = "磁磚採購表_" + this.SanitizeScheduleName(selectionLabel);
			ViewSchedule existing = this.FindTileSchedule(doc, scheduleName, this.SanitizeScheduleName(selectionLabel), "採購");
			if (existing != null) return existing;

			ViewSchedule schedule = this.CreatePurchaseTilePartsSchedule(doc, scheduleName);
			IList<SchedulableField> fields = schedule.Definition.GetSchedulableFields();
			ScheduleField selectionField = this.TryAddScheduleField(doc, schedule, fields, "Tile_SelectionSet", "選取範圍", true);
			if (selectionField != null)
			{
				schedule.Definition.AddFilter(new ScheduleFilter(selectionField.FieldId, ScheduleFilterType.Equal, selectionLabel));
			}
			return schedule;
		}

		private string GetRoomScheduleLabel(Room room)
		{
			if (room == null) return "未指定空間";
			string number = string.IsNullOrWhiteSpace(room.Number) ? room.Id.IntegerValue.ToString() : room.Number;
			string name = string.IsNullOrWhiteSpace(room.Name) ? "未命名" : room.Name;
			return number + " - " + name;
		}

		private string GetTileSizeLabel(double widthMm, double heightMm)
		{
			if (widthMm <= 0.001 || heightMm <= 0.001) return "未指定";
			return string.Format("{0:0.#}x{1:0.#}", Math.Min(widthMm, heightMm), Math.Max(widthMm, heightMm));
		}

		private string BuildTileSelectionLabel(IList<string> selectedHostKeys)
		{
			List<string> keys = (selectedHostKeys ?? new List<string>())
				.Where(key => !string.IsNullOrWhiteSpace(key))
				.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
				.ToList();

			unchecked
			{
				int hash = (int)2166136261;
				foreach (string key in keys)
				{
					foreach (char ch in key)
					{
						hash ^= ch;
						hash *= 16777619;
					}
				}
				return "選取天地牆_" + Math.Abs(hash).ToString("X");
			}
		}

		private double GetTileWasteRatePercent()
		{
			return 0.0;
		}

		private void SetTileWasteParameters(Part part)
		{
			this.SetTileWasteParameters(part, 0.0);
		}

		private void SetTileWasteParameters(Part part, double wasteRate)
		{
			this.SetTileWasteParameters(part, wasteRate, 0.0, 0.0);
		}

		private void SetTileWasteParameters(Part part, double wasteRate, double netQty, double purchaseQty)
		{
			this.SetTileWasteParameters(part, wasteRate, netQty, purchaseQty, 0.0, string.Empty);
		}

		private void SetTileWasteParameters(Part part, double wasteRate, double netQty, double purchaseQty, double areaSqm, string calculationMethod)
		{
			this.SetTileWasteParameters(part, wasteRate, netQty, purchaseQty, areaSqm, calculationMethod, string.Empty, string.Empty, string.Empty);
		}

		private void SetTileWasteParameters(Part part, double wasteRate, double netQty, double purchaseQty, double areaSqm, string calculationMethod, string netQtyText, string purchaseQtyText, string wasteQtyText)
		{
			if (part == null) return;
			if (double.IsNaN(wasteRate) || double.IsInfinity(wasteRate)) wasteRate = 0.0;
			if (wasteRate < 0.0) wasteRate = 0.0;
			if (wasteRate > 100.0) wasteRate = 100.0;
			this.SetSharedParameter(part, "Tile_WasteRate", wasteRate.ToString("0.##") + "%");
			this.SetSharedParameter(part, "Tile_NetQty", netQty);
			this.SetSharedParameter(part, "Tile_QtyWithWaste", purchaseQty);
			this.SetSharedParameter(part, "Tile_AreaM2", areaSqm);
			this.SetSharedParameter(part, "Tile_CalcMethod", calculationMethod ?? string.Empty);
			this.SetSharedParameter(part, "Tile_NetQtyText", netQtyText ?? string.Empty);
			this.SetSharedParameter(part, "Tile_PurchaseQtyText", purchaseQtyText ?? string.Empty);
			this.SetSharedParameter(part, "Tile_WasteQtyText", wasteQtyText ?? string.Empty);

			Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			if (commentParam != null && !commentParam.IsReadOnly)
			{
				string comments = commentParam.AsString();
				if (!string.IsNullOrEmpty(comments) && comments.Contains("Tile_ID:"))
				{
					comments = this.UpsertCommentValue(comments, "Waste_Rate", wasteRate.ToString("0.##") + "%");
					comments = this.UpsertCommentValue(comments, "Net_Qty", netQty.ToString("0.###", CultureInfo.InvariantCulture));
					comments = this.UpsertCommentValue(comments, "Purchase_Qty", purchaseQty.ToString("0.###", CultureInfo.InvariantCulture));
					comments = this.UpsertCommentValue(comments, "Waste_Qty", (purchaseQty - netQty).ToString("0.###", CultureInfo.InvariantCulture));
					comments = this.UpsertCommentValue(comments, "Calc_Method", calculationMethod ?? string.Empty);
					commentParam.Set(comments);
				}
			}
		}

		private void ApplyAutomaticWasteRatesForParts(IList<Part> parts)
		{
			if (parts == null || parts.Count == 0) return;

			Dictionary<string, double> surfaceAreaByGroup = parts
				.Where(part => part != null && part.IsValidObject)
				.GroupBy(part => this.GetTileSurfaceAreaGroupKey(part))
				.ToDictionary(group => group.Key, group => this.GetTileGroupSurfaceAreaSquareMeters(group.ToList()));
			Dictionary<string, TileWasteSummary> combinedWasteByGroup = parts
				.Where(part => part != null && part.IsValidObject)
				.GroupBy(part => this.GetTileSurfaceAreaGroupKey(part))
				.ToDictionary(group => group.Key, group => this.CalculateTileWasteSummary(group.ToList()));

			foreach (IGrouping<string, Part> group in parts
				.Where(part => part != null && part.IsValidObject)
				.GroupBy(part => this.GetTileWasteGroupKey(part)))
			{
				List<Part> groupParts = group.ToList();
				if (groupParts.Count == 0) continue;

				Dictionary<ElementId, double> netByPart = groupParts.ToDictionary(
					part => part.Id,
					part => this.ClampTileRatio(this.GetTileAreaRatio(part)));
				Dictionary<ElementId, double> purchaseByPart = this.CalculatePurchaseContributions(groupParts);
				double netQty = netByPart.Values.Where(ratio => ratio > 0.0001).Sum();
				double purchaseQty = purchaseByPart.Values.Sum();
				double wasteQty = purchaseQty - netQty;
				if (wasteQty < 0.0) wasteQty = 0.0;
				double wasteRate = netQty <= 0.0001 ? 0.0 : (purchaseQty - netQty) / netQty * 100.0;
				string surfaceAreaKey = this.GetTileSurfaceAreaGroupKey(groupParts[0]);
				double surfaceAreaSqm = surfaceAreaByGroup.ContainsKey(surfaceAreaKey) ? surfaceAreaByGroup[surfaceAreaKey] : this.GetTileGroupSurfaceAreaSquareMeters(groupParts);
				TileWasteSummary combinedWaste = combinedWasteByGroup.ContainsKey(surfaceAreaKey)
					? combinedWasteByGroup[surfaceAreaKey]
					: this.CalculateTileWasteSummary(groupParts);
				string surfaceAreaText = surfaceAreaSqm > 0.000001 ? surfaceAreaSqm.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
				string netQtyText = this.FormatTileQuantity(netQty);
				string purchaseQtyText = this.FormatTileQuantity(purchaseQty);
				string wasteQtyText = this.FormatCombinedWasteText(combinedWaste);
				string totalNetQtyText = this.FormatTileQuantity(combinedWaste.NetQty);
				string totalPurchaseQtyText = this.FormatTileQuantity(combinedWaste.PurchaseQty);
				string purchaseCalcMethod = this.GetPurchaseCalculationMethod(combinedWaste);
				string groupMethod = this.GetTileGroupCalculationMethod(groupParts, netQty, purchaseQty, wasteQty);
				foreach (Part part in groupParts)
				{
					double partNetQty = netByPart.ContainsKey(part.Id) ? netByPart[part.Id] : 0.0;
					double partPurchaseQty = purchaseByPart.ContainsKey(part.Id) ? purchaseByPart[part.Id] : 0.0;
					this.SetTileWasteParameters(
						part,
						combinedWaste.WasteRate,
						partNetQty,
						partPurchaseQty,
						this.GetTileAreaSquareMeters(part, partNetQty),
						groupMethod,
						netQtyText,
						purchaseQtyText,
						wasteQtyText);
					this.SetSharedParameter(part, "Tile_SurfaceAreaText", surfaceAreaText);
					this.SetSharedParameter(part, "Tile_TotalNetQtyText", totalNetQtyText);
					this.SetSharedParameter(part, "Tile_TotalPurchaseQtyText", totalPurchaseQtyText);
					this.SetSharedParameter(part, "Tile_PurchaseCalcMethod", purchaseCalcMethod);
				}
			}
		}

		private double GetTileGroupSurfaceAreaSquareMeters(IList<Part> parts)
		{
			if (parts == null || parts.Count == 0) return 0.0;

			Dictionary<string, double> areaByAnchor = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
			foreach (Part part in parts)
			{
				Parameter commentParam = part == null ? null : part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string comments = commentParam == null ? null : commentParam.AsString();
				string surfaceAreaText = this.ParseCommentValue(comments, "Surface_Area");
				if (double.TryParse(surfaceAreaText, NumberStyles.Float, CultureInfo.InvariantCulture, out double surfaceArea) && surfaceArea > 0.000001)
				{
					string anchor = part?.LookupParameter("Anchor_ID")?.AsString();
					if (string.IsNullOrWhiteSpace(anchor)) anchor = this.ParseCommentValue(comments, "Anchor_ID");
					if (string.IsNullOrWhiteSpace(anchor)) anchor = this.ParseCommentValue(comments, "Host_ID");
					if (string.IsNullOrWhiteSpace(anchor)) anchor = part.Id.IntegerValue.ToString();
					if (!areaByAnchor.ContainsKey(anchor)) areaByAnchor[anchor] = surfaceArea;
				}
			}
			if (areaByAnchor.Count > 0) return areaByAnchor.Values.Sum();

			return parts
				.Where(part => part != null && part.IsValidObject)
				.Sum(part => this.GetTileAreaSquareMeters(part, this.ClampTileRatio(this.GetTileAreaRatio(part))));
		}

		private TileWasteSummary CalculateTileWasteSummary(IList<Part> parts)
		{
			TileWasteSummary summary = new TileWasteSummary();
			if (parts == null || parts.Count == 0) return summary;

			List<Part> validParts = parts
				.Where(part => part != null && part.IsValidObject)
				.ToList();
			summary.NetQty = validParts
				.Select(part => this.ClampTileRatio(this.GetTileAreaRatio(part)))
				.Where(ratio => ratio > 0.0001)
				.Sum();
			summary.PurchaseQty = this.CalculatePurchaseContributions(validParts).Values.Sum();
			summary.WasteQty = summary.PurchaseQty - summary.NetQty;
			if (summary.WasteQty < 0.0) summary.WasteQty = 0.0;
			summary.WasteRate = summary.NetQty <= 0.0001 ? 0.0 : summary.WasteQty / summary.NetQty * 100.0;
			return summary;
		}

		private string FormatCombinedWasteText(TileWasteSummary summary)
		{
			if (summary == null) return string.Empty;
			return string.Format(
				CultureInfo.InvariantCulture,
				"合計浪費 {0}片 ({1:0.##}%)",
				this.FormatTileQuantity(summary.WasteQty),
				summary.WasteRate);
		}

		private string GetPurchaseCalculationMethod(TileWasteSummary summary)
		{
			if (summary == null) return string.Empty;
			return string.Format(
				CultureInfo.InvariantCulture,
				"整磚/半磚各1片=採購1片；不足半磚2片=採購1片，單數進位；合計淨{0}片，採購{1}片，浪費{2}片",
				this.FormatTileQuantity(summary.NetQty),
				this.FormatTileQuantity(summary.PurchaseQty),
				this.FormatTileQuantity(summary.WasteQty));
		}

		private Dictionary<ElementId, double> CalculatePurchaseContributions(IList<Part> parts)
		{
			Dictionary<ElementId, double> result = new Dictionary<ElementId, double>();
			if (parts == null) return result;

			foreach (IGrouping<string, Part> typeGroup in parts
				.Where(part => part != null && part.IsValidObject)
				.GroupBy(part => this.GetTileTypeFromPart(part)))
			{
				List<Part> typedParts = typeGroup
					.OrderBy(part => part.Id.IntegerValue)
					.ToList();
				string type = typeGroup.Key ?? string.Empty;
				if (string.Equals(type, "UnderHalfCut", StringComparison.OrdinalIgnoreCase))
				{
					for (int i = 0; i < typedParts.Count; i++)
					{
						result[typedParts[i].Id] = 0.5;
					}
					if (typedParts.Count % 2 == 1 && typedParts.Count > 0)
					{
						result[typedParts[0].Id] += 0.5;
					}
				}
				else
				{
					foreach (Part part in typedParts)
					{
						result[part.Id] = 1.0;
					}
				}
			}

			return result;
		}

		private string FormatTileQuantity(double value)
		{
			if (Math.Abs(value - Math.Round(value)) < 0.0001) return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}

		private string GetTileGroupCalculationMethod(IList<Part> parts, double netQty, double purchaseQty, double wasteQty)
		{
			if (parts == null || parts.Count == 0) return string.Empty;
			string tileType = this.GetTileTypeFromPart(parts[0]);
			int count = parts.Count;
			string rule = this.GetTileCalculationMethod(tileType);
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0}; 數量{1}, 淨{2}片, 採購{3}片, 浪費{4}片",
				rule,
				count,
				this.FormatTileQuantity(netQty),
				this.FormatTileQuantity(purchaseQty),
				this.FormatTileQuantity(wasteQty));
		}

		private string GetTileTypeFromPart(Part part)
		{
			if (part == null) return string.Empty;
			Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			string tileType = part.LookupParameter("Tile_Type")?.AsString();
			if (string.IsNullOrWhiteSpace(tileType)) tileType = this.ParseCommentValue(comments, "Type");
			return tileType ?? string.Empty;
		}

		private string GetTileCalculationMethod(string tileType)
		{
			if (string.Equals(tileType, "Full", StringComparison.OrdinalIgnoreCase)) return "整磚: 1片=採購1片";
			if (string.Equals(tileType, "HalfCut", StringComparison.OrdinalIgnoreCase)) return "半磚: 1片=採購1片";
			if (string.Equals(tileType, "UnderHalfCut", StringComparison.OrdinalIgnoreCase)) return "不足半磚: 2片=採購1片，單數進位";
			return "未分類: 1片=採購1片";
		}

		private double GetTileAreaSquareMeters(Part part, double areaRatio)
		{
			if (part == null) return 0.0;
			Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			string areaText = this.ParseCommentValue(comments, "Area");
			if (!string.IsNullOrWhiteSpace(areaText))
			{
				if (double.TryParse(areaText, NumberStyles.Float, CultureInfo.InvariantCulture, out double area)) return area;
				if (double.TryParse(areaText, out area)) return area;
			}

			double tileAreaSqm = this.GetTileAreaFromPartSizeSquareMeters(part);
			if (tileAreaSqm > 0.000001 && areaRatio > 0.0001) return tileAreaSqm * areaRatio;

			try
			{
				Parameter areaParam = part.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
				if (areaParam != null && areaParam.HasValue) return areaParam.AsDouble() * 0.09290304;
			}
			catch { }

			return 0.0;
		}

		private double GetTileAreaFromPartSizeSquareMeters(Part part)
		{
			if (part == null) return 0.0;
			Parameter widthParam = part.LookupParameter("Tile_Width");
			Parameter heightParam = part.LookupParameter("Tile_Height");
			double widthFeet = widthParam != null && widthParam.HasValue ? widthParam.AsDouble() : 0.0;
			double heightFeet = heightParam != null && heightParam.HasValue ? heightParam.AsDouble() : 0.0;
			if (widthFeet > 0.0001 && heightFeet > 0.0001) return widthFeet * heightFeet * 0.09290304;

			string size = part.LookupParameter("Tile_Size")?.AsString();
			if (TileDimensionDetector.ParseDimensions(size, out double widthMm, out double heightMm))
			{
				return widthMm * heightMm / 1000000.0;
			}

			return 0.0;
		}

		private double ClampTileRatio(double ratio)
		{
			if (double.IsNaN(ratio) || double.IsInfinity(ratio)) return 1.0;
			if (ratio < 0.0) return 0.0;
			if (ratio > 1.0) return 1.0;
			return ratio;
		}

		private double EstimatePurchasedTileCount(IList<double> ratios)
		{
			if (ratios == null || ratios.Count == 0) return 0.0;

			int fullTiles = ratios.Count(ratio => ratio >= 0.999);
			List<double> cuts = ratios
				.Where(ratio => ratio > 0.0001 && ratio < 0.999)
				.OrderByDescending(ratio => ratio)
				.ToList();
			List<double> remainingBins = new List<double>();

			foreach (double cut in cuts)
			{
				bool placed = false;
				for (int i = 0; i < remainingBins.Count; i++)
				{
					if (remainingBins[i] + 0.0001 >= cut)
					{
						remainingBins[i] -= cut;
						placed = true;
						break;
					}
				}
				if (!placed)
				{
					remainingBins.Add(1.0 - cut);
				}
			}

			return fullTiles + remainingBins.Count;
		}

		private string GetTileWasteGroupKey(Part part)
		{
			Parameter commentParam = part == null ? null : part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			string room = part?.LookupParameter("Room_ID")?.AsString();
			if (string.IsNullOrWhiteSpace(room)) room = this.ParseCommentValue(comments, "Room_ID");
			string surface = part?.LookupParameter("Tile_Surface")?.AsString();
			if (string.IsNullOrWhiteSpace(surface)) surface = this.ParseCommentValue(comments, "Surface");
			string material = part?.LookupParameter("Tile_Material")?.AsString();
			string size = part?.LookupParameter("Tile_Size")?.AsString();
			if (string.IsNullOrWhiteSpace(size)) size = this.ParseCommentValue(comments, "Size");
			string type = this.GetTileTypeFromPart(part);
			return string.Join("|", new[] { room ?? string.Empty, surface ?? string.Empty, material ?? string.Empty, size ?? string.Empty, type ?? string.Empty });
		}

		private string GetTileSurfaceAreaGroupKey(Part part)
		{
			Parameter commentParam = part == null ? null : part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			string room = part?.LookupParameter("Room_ID")?.AsString();
			if (string.IsNullOrWhiteSpace(room)) room = this.ParseCommentValue(comments, "Room_ID");
			string surface = part?.LookupParameter("Tile_Surface")?.AsString();
			if (string.IsNullOrWhiteSpace(surface)) surface = this.ParseCommentValue(comments, "Surface");
			string material = part?.LookupParameter("Tile_Material")?.AsString();
			string size = part?.LookupParameter("Tile_Size")?.AsString();
			if (string.IsNullOrWhiteSpace(size)) size = this.ParseCommentValue(comments, "Size");
			return string.Join("|", new[] { room ?? string.Empty, surface ?? string.Empty, material ?? string.Empty, size ?? string.Empty });
		}

		private double GetTileAreaRatio(Part part)
		{
			if (part == null) return 1.0;
			Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			string ratioText = this.ParseCommentValue(comments, "Area_Ratio");
			if (!string.IsNullOrWhiteSpace(ratioText))
			{
				if (double.TryParse(ratioText, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio)) return ratio;
				if (double.TryParse(ratioText, out ratio)) return ratio;
			}

			string tileType = part.LookupParameter("Tile_Type")?.AsString();
			if (string.IsNullOrWhiteSpace(tileType)) tileType = this.ParseCommentValue(comments, "Type");
			if (string.Equals(tileType, "Full", StringComparison.OrdinalIgnoreCase)) return 1.0;
			if (string.Equals(tileType, "HalfCut", StringComparison.OrdinalIgnoreCase)) return 0.5;
			if (string.Equals(tileType, "UnderHalfCut", StringComparison.OrdinalIgnoreCase)) return 0.25;
			return 1.0;
		}

		private string GetTileSizeLabelFromPart(Part part)
		{
			if (part == null) return "未指定";
			try
			{
				Parameter widthParam = part.LookupParameter("Tile_Width");
				Parameter heightParam = part.LookupParameter("Tile_Height");
				double widthMm = widthParam != null && widthParam.HasValue ? widthParam.AsDouble() * 304.8 : 0.0;
				double heightMm = heightParam != null && heightParam.HasValue ? heightParam.AsDouble() * 304.8 : 0.0;
				return this.GetTileSizeLabel(widthMm, heightMm);
			}
			catch
			{
				return "未指定";
			}
		}

		private void NormalizeTileScheduleFieldsForPart(Part part)
		{
			if (part == null) return;

			Parameter widthParam = part.LookupParameter("Tile_Width");
			Parameter heightParam = part.LookupParameter("Tile_Height");
			double widthMm = widthParam != null && widthParam.HasValue ? widthParam.AsDouble() * 304.8 : 0.0;
			double heightMm = heightParam != null && heightParam.HasValue ? heightParam.AsDouble() * 304.8 : 0.0;

			string materialName = part.LookupParameter("Tile_Material")?.AsString();
			TilePatternParams materialParams = this.ParseParamsFromMatName(materialName);
			if (materialParams != null)
			{
				if (widthMm <= 0.001) widthMm = materialParams.TileWidth;
				if (heightMm <= 0.001) heightMm = materialParams.TileHeight;
			}

			if ((widthMm <= 0.001 || heightMm <= 0.001) && !string.IsNullOrWhiteSpace(materialName))
			{
				if (TileDimensionDetector.ParseDimensions(materialName, out double parsedWidth, out double parsedHeight))
				{
					widthMm = parsedWidth;
					heightMm = parsedHeight;
				}
			}

			if (widthMm > 0.001 && heightMm > 0.001)
			{
				string sizeLabel = this.GetTileSizeLabel(widthMm, heightMm);
				this.SetSharedParameter(part, "Tile_Size", sizeLabel);

				TilePatternStyle style = materialParams == null ? TilePatternStyle.Stack : materialParams.Style;
				this.SetSharedParameter(part, "Tile_Material", string.Format(
					"TileMAT_{0}_{1}",
					(int)style,
					sizeLabel));
			}
		}

		private List<Part> UpdateAndCollectTilePartsForRoom(Document doc, Room room, string roomLabel)
		{
			List<Part> result = new List<Part>();
			foreach (Part part in new FilteredElementCollector(doc)
				.OfClass(typeof(Part))
				.WhereElementIsNotElementType()
				.OfType<Part>())
			{
				if (this.IsPartExcludedByMaker(part)) continue;

				Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string comments = commentParam == null ? null : commentParam.AsString();
				bool isTilePart = !string.IsNullOrEmpty(comments) && comments.Contains("Tile_ID:");
				Parameter roomParam = part.LookupParameter("Room_ID");
				string currentRoomValue = roomParam != null && roomParam.HasValue ? roomParam.AsString() : this.ParseCommentValue(comments, "Room_ID");
				bool alreadyInRoom = string.Equals(currentRoomValue, roomLabel, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(currentRoomValue, room.UniqueId, StringComparison.OrdinalIgnoreCase);

				bool detectedInRoom = this.IsTilePartInRoom(doc, part, room);
				if (!alreadyInRoom && !detectedInRoom) continue;

				this.SetSharedParameter(part, "Room_ID", roomLabel);
				string anchorId = this.ParseCommentValue(comments, "Anchor_ID");
				if (string.IsNullOrWhiteSpace(anchorId)) anchorId = this.ParseCommentValue(comments, "Host_ID");
				if (!string.IsNullOrWhiteSpace(anchorId))
				{
					this.SetSharedParameter(part, "Anchor_ID", anchorId);
				}
				if (string.IsNullOrEmpty(part.LookupParameter("Tile_Surface")?.AsString()))
				{
					this.SetSharedParameter(part, "Tile_Surface", this.GetTileSurfaceLabelFromPart(part));
				}
				if (string.IsNullOrEmpty(part.LookupParameter("Tile_Size")?.AsString()))
				{
					this.SetSharedParameter(part, "Tile_Size", this.GetTileSizeLabelFromPart(part));
				}
				this.NormalizeTileScheduleFieldsForPart(part);
				this.SetTileWasteParameters(part);

				if (commentParam != null && !commentParam.IsReadOnly)
				{
					string updated = this.UpsertCommentValue(comments, "Room_ID", roomLabel);
					commentParam.Set(updated);
				}

				if (isTilePart || alreadyInRoom || detectedInRoom)
				{
					result.Add(part);
				}
			}
			this.ApplyAutomaticWasteRatesForParts(result);
			return result;
		}

		private bool IsTilePartInRoom(Document doc, Part part, Room room)
		{
			if (doc == null || part == null || room == null) return false;
			foreach (XYZ point in this.GetPartRoomProbePoints(part))
			{
				try
				{
					Room found = doc.GetRoomAtPoint(point);
					if (found != null && found.Id == room.Id) return true;
				}
				catch { }
			}
			return false;
		}

		private List<XYZ> GetPartRoomProbePoints(Part part)
		{
			List<XYZ> points = new List<XYZ>();
			BoundingBoxXYZ box = null;
			try { box = part.get_BoundingBox(null); } catch { }
			if (box == null) return points;

			XYZ center = (box.Min + box.Max) * 0.5;
			points.Add(center);
			XYZ normal = this.GetPartLargestFaceNormal(part);
			if (normal != null && normal.GetLength() > 0.001)
			{
				normal = normal.Normalize();
				points.Add(center + normal * 0.25);
				points.Add(center - normal * 0.25);
				points.Add(center + normal * 0.75);
				points.Add(center - normal * 0.75);
			}
			points.Add(center + XYZ.BasisZ * 0.5);
			points.Add(center - XYZ.BasisZ * 0.5);
			points.Add(center + XYZ.BasisX * 0.5);
			points.Add(center - XYZ.BasisX * 0.5);
			points.Add(center + XYZ.BasisY * 0.5);
			points.Add(center - XYZ.BasisY * 0.5);
			return points;
		}

		private XYZ GetPartLargestFaceNormal(Part part)
		{
			double bestArea = 0.0;
			XYZ bestNormal = null;
			try
			{
				GeometryElement geometry = part.get_Geometry(new Options { ComputeReferences = false, IncludeNonVisibleObjects = true });
				foreach (GeometryObject obj in geometry ?? Enumerable.Empty<GeometryObject>())
				{
					Solid solid = obj as Solid;
					if (solid == null) continue;
					foreach (Face face in solid.Faces)
					{
						PlanarFace planarFace = face as PlanarFace;
						if (planarFace == null) continue;
						if (planarFace.Area > bestArea)
						{
							bestArea = planarFace.Area;
							bestNormal = planarFace.FaceNormal;
						}
					}
				}
			}
			catch { }
			return bestNormal;
		}

		private string GetTileSurfaceLabelFromPart(Part part)
		{
			Parameter sourceParam = part.get_Parameter(BuiltInParameter.DPART_ORIGINAL_CATEGORY);
			string sourceCategory = sourceParam == null ? string.Empty : sourceParam.AsValueString();
			if (!string.IsNullOrEmpty(sourceCategory))
			{
				if (sourceCategory.Contains("牆") || sourceCategory.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0) return "牆面";
				if (sourceCategory.Contains("樓板") || sourceCategory.Contains("地") || sourceCategory.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0) return "地坪";
				if (sourceCategory.Contains("天花") || sourceCategory.Contains("屋頂") || sourceCategory.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0 || sourceCategory.IndexOf("Roof", StringComparison.OrdinalIgnoreCase) >= 0) return "天花板";
			}
			return "未分類";
		}

		private string UpsertCommentValue(string comments, string key, string value)
		{
			List<string> parts = string.IsNullOrWhiteSpace(comments)
				? new List<string>()
				: comments.Split('|').Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
			string prefix = key + ":";
			bool replaced = false;
			for (int i = 0; i < parts.Count; i++)
			{
				if (parts[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					parts[i] = prefix + value;
					replaced = true;
				}
			}
			if (!replaced) parts.Add(prefix + value);
			return string.Join("|", parts);
		}

		private string SanitizeScheduleName(string value)
		{
			return Regex.Replace(value ?? "未命名", @"[\\/:*?""<>|{};`~\[\]]", "_");
		}

		private ViewSchedule FindTileSchedule(Document doc, string exactName, string scopeText, string kindText)
		{
			if (doc == null) return null;
			List<ViewSchedule> schedules = new FilteredElementCollector(doc)
				.OfClass(typeof(ViewSchedule))
				.Cast<ViewSchedule>()
				.Where(schedule => schedule != null && !schedule.IsTemplate && !schedule.IsTitleblockRevisionSchedule)
				.ToList();

			ViewSchedule exact = schedules.FirstOrDefault(schedule =>
				string.Equals(schedule.Name, exactName, StringComparison.OrdinalIgnoreCase));
			if (exact != null) return exact;

			return schedules.FirstOrDefault(schedule =>
				(string.IsNullOrWhiteSpace(kindText) || schedule.Name.IndexOf(kindText, StringComparison.OrdinalIgnoreCase) >= 0) &&
				(string.IsNullOrWhiteSpace(scopeText) || schedule.Name.IndexOf(scopeText, StringComparison.OrdinalIgnoreCase) >= 0));
		}

		private bool TileScheduleExists(Document doc, string scopeText, string kindText)
		{
			return this.FindTileSchedule(doc, null, this.SanitizeScheduleName(scopeText), kindText) != null ||
				this.FindTileSchedule(doc, null, scopeText, kindText) != null;
		}

		private ViewSchedule FindExistingTileCheckSchedule(Document doc, string scopeText)
		{
			if (doc == null || string.IsNullOrWhiteSpace(scopeText)) return null;
			string safeScope = this.SanitizeScheduleName(scopeText);
			return this.FindTileSchedule(doc, "磁磚檢核表_" + safeScope, safeScope, "檢核") ??
				this.FindTileSchedule(doc, null, safeScope, "檢核") ??
				this.FindTileSchedule(doc, null, scopeText, "檢核") ??
				this.FindTileSchedule(doc, null, safeScope, "瑼Ｘ") ??
				this.FindTileSchedule(doc, null, scopeText, "瑼Ｘ");
		}

		private void PlaceScheduleOnSheetIfMissing(Document doc, ViewSheet sheet, ViewSchedule schedule, XYZ location)
		{
			if (doc == null || sheet == null || schedule == null) return;
			try
			{
				bool alreadyPlaced = new FilteredElementCollector(doc)
					.OfClass(typeof(ScheduleSheetInstance))
					.Cast<ScheduleSheetInstance>()
					.Any(instance => instance.OwnerViewId == sheet.Id && instance.ScheduleId == schedule.Id);
				if (!alreadyPlaced)
				{
					ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, location);
				}
			}
			catch { }
		}

		private ViewSchedule CreateBaseTilePartsSchedule(Document doc, string scheduleName)
		{
			ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Parts));
			schedule.Name = scheduleName;

			IList<SchedulableField> fields = schedule.Definition.GetSchedulableFields();
			ScheduleField commentField = this.TryAddScheduleField(doc, schedule, fields, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, null, true);
			ScheduleField surfaceField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Surface", "Surface", false);
			ScheduleField materialField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Material", "Material", false);
			ScheduleField typeField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Type", "Type", false);
			ScheduleField sizeField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Size", "Size", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_WasteQtyText", "浪費片數/率", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_SurfaceAreaText", "鋪貼面積(m2)", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_NetQtyText", "淨面積片數", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_PurchaseQtyText", "採購片數", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_CalcMethod", "計算方式", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_Width", "Width(mm)", true);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_Height", "Height(mm)", true);
			this.TryAddCountScheduleField(doc, schedule, fields);

			schedule.Definition.IsItemized = false;
			schedule.Definition.ShowGrandTotal = true;
			foreach (ScheduleField field in new[] { surfaceField, materialField, typeField, sizeField }.Where(f => f != null))
			{
				this.TryAddScheduleSortGroupField(schedule, new ScheduleSortGroupField(field.FieldId) { ShowHeader = field == surfaceField, ShowFooter = field == surfaceField });
			}
			if (commentField != null)
			{
				schedule.Definition.AddFilter(new ScheduleFilter(commentField.FieldId, ScheduleFilterType.Contains, "Tile_ID:"));
			}

			return schedule;
		}

		private ViewSchedule CreatePurchaseTilePartsSchedule(Document doc, string scheduleName)
		{
			ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Parts));
			schedule.Name = scheduleName;

			IList<SchedulableField> fields = schedule.Definition.GetSchedulableFields();
			ScheduleField commentField = this.TryAddScheduleField(doc, schedule, fields, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, null, true);
			ScheduleField roomField = this.TryAddScheduleField(doc, schedule, fields, "Room_ID", "空間", false);
			ScheduleField surfaceField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Surface", "位置", false);
			ScheduleField materialField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Material", "磁磚材質", false);
			ScheduleField sizeField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Size", "規格", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_SurfaceAreaText", "鋪貼面積(m2)", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_TotalNetQtyText", "淨需求片數", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_TotalPurchaseQtyText", "建議採購片數", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_WasteQtyText", "浪費片數/率", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_PurchaseCalcMethod", "採購計算方式", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_Width", "寬(mm)", true);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_Height", "高(mm)", true);

			schedule.Definition.IsItemized = false;
			schedule.Definition.ShowGrandTotal = false;
			foreach (ScheduleField field in new[] { roomField, surfaceField, materialField, sizeField }.Where(f => f != null))
			{
				this.TryAddScheduleSortGroupField(schedule, new ScheduleSortGroupField(field.FieldId) { ShowHeader = field == surfaceField, ShowFooter = false });
			}
			if (commentField != null)
			{
				schedule.Definition.AddFilter(new ScheduleFilter(commentField.FieldId, ScheduleFilterType.Contains, "Tile_ID:"));
			}

			return schedule;
		}

		private ViewSchedule CreateRoomTilePartsSchedule(Document doc, string roomLabel)
		{
			string safeRoomName = this.SanitizeScheduleName(roomLabel);
			string scheduleName = "磁磚檢核表_" + safeRoomName;
			ViewSchedule existing = this.FindTileSchedule(doc, scheduleName, safeRoomName, "檢核");
			if (existing != null) return existing;

			ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Parts));
			schedule.Name = scheduleName;

			IList<SchedulableField> fields = schedule.Definition.GetSchedulableFields();
			ScheduleField commentField = this.TryAddScheduleField(doc, schedule, fields, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, null, true);
			ScheduleField roomField = this.TryAddScheduleField(doc, schedule, fields, "Room_ID", "空間", false);
			ScheduleField surfaceField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Surface", "位置", false);
			ScheduleField materialField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Material", "磁磚材質", false);
			ScheduleField typeField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Type", "類型", false);
			ScheduleField sizeField = this.TryAddScheduleField(doc, schedule, fields, "Tile_Size", "規格", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_Width", "寬(mm)", true);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_Height", "高(mm)", true);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_WasteQtyText", "浪費片數/率", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_SurfaceAreaText", "鋪貼面積(m2)", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_NetQtyText", "淨面積片數", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_PurchaseQtyText", "採購片數", false);
			this.TryAddScheduleField(doc, schedule, fields, "Tile_CalcMethod", "計算方式", false);
			this.TryAddCountScheduleField(doc, schedule, fields);

			schedule.Definition.IsItemized = false;
			schedule.Definition.ShowGrandTotal = true;
			foreach (ScheduleField field in new[] { surfaceField, materialField, typeField, sizeField }.Where(f => f != null))
			{
				this.TryAddScheduleSortGroupField(schedule, new ScheduleSortGroupField(field.FieldId) { ShowHeader = field == surfaceField, ShowFooter = field == surfaceField });
			}
			if (commentField != null)
			{
				schedule.Definition.AddFilter(new ScheduleFilter(commentField.FieldId, ScheduleFilterType.Contains, "Tile_ID:"));
			}
			if (roomField != null)
			{
				schedule.Definition.AddFilter(new ScheduleFilter(roomField.FieldId, ScheduleFilterType.Equal, roomLabel));
			}
			return schedule;
		}

		private ViewSchedule CreateRoomTilePurchaseSchedule(Document doc, string roomLabel)
		{
			string safeRoomName = this.SanitizeScheduleName(roomLabel);
			string scheduleName = "磁磚採購表_" + safeRoomName;
			ViewSchedule existing = this.FindTileSchedule(doc, scheduleName, safeRoomName, "採購");
			if (existing != null) return existing;

			ViewSchedule schedule = this.CreatePurchaseTilePartsSchedule(doc, scheduleName);
			IList<SchedulableField> fields = schedule.Definition.GetSchedulableFields();
			ScheduleField roomField = this.TryAddScheduleField(doc, schedule, fields, "Room_ID", "空間", false);
			if (roomField != null)
			{
				schedule.Definition.AddFilter(new ScheduleFilter(roomField.FieldId, ScheduleFilterType.Equal, roomLabel));
			}
			return schedule;
		}

		private bool TryAddScheduleSortGroupField(ViewSchedule schedule, ScheduleSortGroupField sortGroupField)
		{
			if (schedule == null || sortGroupField == null) return false;
			try
			{
				schedule.Definition.AddSortGroupField(sortGroupField);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private ScheduleField TryAddScheduleField(Document doc, ViewSchedule schedule, IList<SchedulableField> fields, string parameterName, string heading, bool hidden)
		{
			if (schedule == null || schedule.Definition == null) return null;
			for (int i = 0; i < schedule.Definition.GetFieldCount(); i++)
			{
				try
				{
					ScheduleField existing = schedule.Definition.GetField(i);
					if (existing != null &&
						(string.Equals(existing.GetName(), parameterName, StringComparison.OrdinalIgnoreCase) ||
						(!string.IsNullOrEmpty(heading) && string.Equals(existing.GetName(), heading, StringComparison.OrdinalIgnoreCase))))
					{
						if (!string.IsNullOrEmpty(heading)) existing.ColumnHeading = heading;
						existing.IsHidden = hidden;
						return existing;
					}
				}
				catch { }
			}

			foreach (SchedulableField field in fields)
			{
				if (!string.Equals(field.GetName(doc), parameterName, StringComparison.OrdinalIgnoreCase)) continue;
				try
				{
					ScheduleField scheduleField = schedule.Definition.AddField(field);
					if (!string.IsNullOrEmpty(heading)) scheduleField.ColumnHeading = heading;
					scheduleField.IsHidden = hidden;
					return scheduleField;
				}
				catch { return null; }
			}
			return null;
		}

		private ScheduleField TryAddScheduleField(Document doc, ViewSchedule schedule, IList<SchedulableField> fields, BuiltInParameter parameter, string heading, bool hidden)
		{
			foreach (SchedulableField field in fields)
			{
				if (field.ParameterId != new ElementId(parameter)) continue;
				try
				{
					ScheduleField scheduleField = schedule.Definition.AddField(field);
					if (!string.IsNullOrEmpty(heading)) scheduleField.ColumnHeading = heading;
					scheduleField.IsHidden = hidden;
					return scheduleField;
				}
				catch { return null; }
			}
			return null;
		}

		private ScheduleField TryAddScheduleTotalField(Document doc, ViewSchedule schedule, IList<SchedulableField> fields, string parameterName, string heading, bool hidden)
		{
			ScheduleField field = this.TryAddScheduleField(doc, schedule, fields, parameterName, heading, hidden);
			if (field != null)
			{
				try
				{
					field.DisplayType = ScheduleFieldDisplayType.Totals;
				}
				catch { }
			}
			return field;
		}

		private void TryAddCountScheduleField(Document doc, ViewSchedule schedule, IList<SchedulableField> fields)
		{
			foreach (SchedulableField field in fields)
			{
				string name = field.GetName(doc);
				if (!name.Equals("Count", StringComparison.OrdinalIgnoreCase) && !name.Equals("數量", StringComparison.OrdinalIgnoreCase) && !name.Equals("合計", StringComparison.OrdinalIgnoreCase)) continue;
				try
				{
					ScheduleField countField = schedule.Definition.AddField(field);
					countField.ColumnHeading = "數量";
					countField.DisplayType = ScheduleFieldDisplayType.Totals;
				}
				catch { }
				return;
			}
		}
		private void DoExportExcel(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("正在準備匯出統計數據...");
				List<Element> list = new List<Element>();
				list.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().ToElements());
				list.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Floor)).WhereElementIsNotElementType().ToElements());
				list.AddRange(new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).WhereElementIsNotElementType().ToElements());
				List<TileSyncEventHandler.ExportTileData> list2 = new List<TileSyncEventHandler.ExportTileData>();
				foreach (Element current in list)
				{
					Parameter parameter = current.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
					string text = (parameter != null) ? parameter.AsString() : null;
					bool flag = string.IsNullOrEmpty(text) || !text.Contains("Tile_ID:");
					if (!flag)
					{
						string tile_ID = this.ParseCommentValue(text, "Tile_ID");
						string anchor_ID = this.ParseCommentValue(text, "Anchor_ID");
						string room_ID = this.ParseCommentValue(text, "Room_ID");
						string surface_ID = this.ParseCommentValue(text, "Surface_ID");
						string tile_Type = this.ParseCommentValue(text, "Type");
						string host_ID = this.ParseCommentValue(text, "Host_ID");
						string s = this.ParseCommentValue(text, "Area");
						double area;
						double.TryParse(s, out area);
						double num = 0.0;
						double num2 = 0.0;
						double thickness = 0.0;
						Parameter parameter2 = current.LookupParameter("Tile_Width");
						Parameter parameter3 = current.LookupParameter("Tile_Height");
						Parameter parameter4 = current.LookupParameter("Tile_Thickness");
						bool flag2 = parameter2 != null;
						if (flag2)
						{
							num = parameter2.AsDouble() * 304.8;
						}
						bool flag3 = parameter3 != null;
						if (flag3)
						{
							num2 = parameter3.AsDouble() * 304.8;
						}
						bool flag4 = parameter4 != null;
						if (flag4)
						{
							thickness = parameter4.AsDouble() * 304.8;
						}
						bool flag5 = num < 0.1 || num2 < 0.1;
						if (flag5)
						{
							num = 300.0;
							num2 = 300.0;
							thickness = 10.0;
						}
						list2.Add(new TileSyncEventHandler.ExportTileData
						{
							ElementId = current.Id.ToString(),
							Tile_ID = tile_ID,
							Anchor_ID = anchor_ID,
							Room_ID = room_ID,
							Surface_ID = surface_ID,
							Tile_Type = tile_Type,
							Host_ID = host_ID,
							Area = area,
							Width = num,
							Height = num2,
							Thickness = thickness
						});
					}
				}
				bool flag6 = list2.Count == 0;
				if (flag6)
				{
					TaskDialog.Show("提示", "模型中沒有偵測到任何 3D 磁磚實體，請先建立 3D 實體再進行匯出。");
					this.ViewModel.SetStatus("沒有可匯出的磁磚實體。");
				}
				else
				{
					SaveFileDialog saveFileDialog = new SaveFileDialog();
					saveFileDialog.Filter = "CSV檔案 (*.csv)|*.csv";
					saveFileDialog.FileName = "Revit磁磚統計報表_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
					saveFileDialog.Title = "儲存磁磚估算 Excel 報表";
					bool flag7 = !saveFileDialog.ShowDialog().GetValueOrDefault();
					if (flag7)
					{
						this.ViewModel.SetStatus("取消匯出。");
					}
					else
					{
						string fileName = saveFileDialog.FileName;
						using (StreamWriter streamWriter = new StreamWriter(fileName, false, Encoding.UTF8))
						{
							streamWriter.Write('\ufeff');
							streamWriter.WriteLine("Revit 磁磚配置統計報表");
							streamWriter.WriteLine(string.Format("匯出時間: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
							streamWriter.WriteLine("專案名稱: " + doc.Title);
							streamWriter.WriteLine(string.Format("磁磚總數: {0} 塊", list2.Count));
							streamWriter.WriteLine();
							streamWriter.WriteLine("房間彙總統計表");
							streamWriter.WriteLine("\"房間名稱\",\"定位編號\",\"規格尺寸\",\"總數量\",\"整磚數量\",\"裁切數量\",\"貼面面積(㎡)\",\"磁磚總面積(㎡)\",\"損耗率\"");
							Dictionary<string, List<TileSyncEventHandler.ExportTileData>> dictionary = new Dictionary<string, List<TileSyncEventHandler.ExportTileData>>();
							foreach (TileSyncEventHandler.ExportTileData current2 in list2)
							{
								string key = string.Format("{0}_{1}_{2:F0}x{3:F0}x{4:F1}", new object[]
								{
									current2.Room_ID,
									current2.Anchor_ID,
									current2.Width,
									current2.Height,
									current2.Thickness
								});
								bool flag8 = !dictionary.ContainsKey(key);
								if (flag8)
								{
									dictionary[key] = new List<TileSyncEventHandler.ExportTileData>();
								}
								dictionary[key].Add(current2);
							}
							foreach (KeyValuePair<string, List<TileSyncEventHandler.ExportTileData>> current3 in dictionary)
							{
								List<TileSyncEventHandler.ExportTileData> value = current3.Value;
								TileSyncEventHandler.ExportTileData exportTileData = value[0];
								int count = value.Count;
								int num3 = 0;
								int num4 = 0;
								double num5 = 0.0;
								double num6 = exportTileData.Width * exportTileData.Height / 1000000.0;
								foreach (TileSyncEventHandler.ExportTileData current4 in value)
								{
									num5 += current4.Area;
									bool flag9 = current4.Tile_Type.Equals("Full", StringComparison.OrdinalIgnoreCase);
									if (flag9)
									{
										num3++;
									}
									else
									{
										num4++;
									}
								}
								double num7 = (double)count * num6;
								double num8 = Math.Max(0.0, num7 - num5);
								double num9 = (num7 > 1E-06) ? (num8 / num7 * 100.0) : 0.0;
								streamWriter.WriteLine(string.Format("\"{0}\",\"{1}\",\"{2:F0}x{3:F0}x{4:F1}\",{5},{6},{7},{8:F3},{9:F3},{10:F1}%", new object[]
								{
									exportTileData.Room_ID,
									exportTileData.Anchor_ID,
									exportTileData.Width,
									exportTileData.Height,
									exportTileData.Thickness,
									count,
									num3,
									num4,
									num5,
									num7,
									num9
								}));
							}
							streamWriter.WriteLine();
							streamWriter.WriteLine("磁磚單塊明細表");
							streamWriter.WriteLine("\"元素ID\",\"磁磚編號\",\"房間名稱\",\"定位編號\",\"面編號\",\"磁磚類型\",\"規格寬\",\"規格高\",\"厚度\",\"實際面積\",\"主體ID\"");
							foreach (TileSyncEventHandler.ExportTileData current5 in list2)
							{
								streamWriter.WriteLine(string.Format("{0},\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",{6:F1},{7:F1},{8:F1},{9:F6},\"{10}\"", new object[]
								{
									current5.ElementId,
									current5.Tile_ID,
									current5.Room_ID,
									current5.Anchor_ID,
									current5.Surface_ID,
									current5.Tile_Type,
									current5.Width,
									current5.Height,
									current5.Thickness,
									current5.Area,
									current5.Host_ID
								}));
							}
						}
						this.ViewModel.SetStatus("已成功匯出至：" + Path.GetFileName(fileName));
						TaskDialog.Show("提示", "匯出 CSV 報表成功。");
					}
				}
			}
			catch (Exception ex)
			{
				TaskDialog.Show("匯出報表錯誤", ex.ToString());
				this.ViewModel.SetStatus("匯出報表失敗: " + ex.Message);
			}
		}
		private void DoConvertToEditable(UIApplication app, UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("磁磚配置系統作業");
				string str = "";
				Result result = ConvertToEditableCommand.RunConvert(app, uidoc, doc, ref str);
				bool flag = result == Result.Succeeded;
				if (flag)
				{
					this.ViewModel.SetStatus("請選取有填充線的牆面。");
				}
				else
				{
					bool flag2 = result == Result.Cancelled;
					if (flag2)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						this.ViewModel.SetStatus("磁磚配置系統作業" + str);
					}
				}
			}
			catch (Exception ex)
			{
				this.ViewModel.SetStatus("磁磚配置系統作業" + ex.Message);
			}
		}
		private void DoChangeLocalTileMaterial(UIDocument uidoc, Document doc)
		{
			try
			{
				this.ViewModel.SetStatus("3D 磁磚");
				IList<Reference> list = null;
				try
				{
					list = uidoc.Selection.PickObjects(ObjectType.Element, new TileSelectionFilter(), "3D 磁磚");
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					this.ViewModel.SetStatus("處理中...");
					return;
				}
				bool flag = list == null || list.Count == 0;
				if (flag)
				{
					this.ViewModel.SetStatus("處理中...");
				}
				else
				{
					List<Material> list2 = new List<Material>();
					using (IEnumerator<Element> enumerator = new FilteredElementCollector(doc).OfClass(typeof(Material)).GetEnumerator())
					{
						while (enumerator.MoveNext())
						{
							Material material = (Material)enumerator.Current;
							bool flag2 = material.Name.StartsWith("TileMAT_", StringComparison.OrdinalIgnoreCase) || material.Name.Equals("Tile_Default_Material", StringComparison.OrdinalIgnoreCase);
							if (flag2)
							{
								list2.Add(material);
							}
						}
					}
					bool flag3 = list2.Count == 0;
					if (flag3)
					{
						this.ViewModel.SetStatus("處理中...");
					}
					else
					{
						TaskDialog taskDialog = new TaskDialog("房間磁磚配置");
						taskDialog.MainInstruction = string.Format("已選取 {0} 塊磁磚。", list.Count);
						taskDialog.MainContent = "請選取已繪製填充線的面。";
						int num = Math.Min(list2.Count, 4);
						for (int i = 0; i < num; i++)
						{
							taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1 + i, list2[i].Name);
						}
						taskDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
						TaskDialogResult taskDialogResult = taskDialog.Show();
						bool flag4 = taskDialogResult == TaskDialogResult.Cancel;
						if (flag4)
						{
							this.ViewModel.SetStatus("處理中...");
						}
						else
						{
							int num2 = taskDialogResult - TaskDialogResult.CommandLink1;
							bool flag5 = num2 < 0 || num2 >= list2.Count;
							if (flag5)
							{
								this.ViewModel.SetStatus("處理中...");
							}
							else
							{
								ElementId id = list2[num2].Id;
								string name = list2[num2].Name;
								int num3 = 0;
								using (Transaction transaction = new Transaction(doc, "磁磚配置系統操作"))
								{
									transaction.Start();
									GeometryGenerator geometryGenerator = new GeometryGenerator(doc);
									foreach (Reference current in list)
									{
										Element element = doc.GetElement(current.ElementId);
										bool flag6 = element == null;
										if (!flag6)
										{
											Wall wall = element as Wall;
											bool flag7 = wall != null;
											if (flag7)
											{
												try
												{
													WallType wallType = wall.WallType;
													CompoundStructure compoundStructure = wallType.GetCompoundStructure();
													double thicknessFeet = (compoundStructure != null && compoundStructure.LayerCount > 0) ? compoundStructure.GetLayerWidth(0) : 0.03280839895013123;
													ElementId orCreateTileWallType = geometryGenerator.GetOrCreateTileWallType(doc, thicknessFeet, id, name);
													bool flag8 = orCreateTileWallType != ElementId.InvalidElementId;
													if (flag8)
													{
														wall.WallType = (doc.GetElement(orCreateTileWallType) as WallType);
														this.SetSharedParameter(wall, "Tile_Material", name);
														num3++;
													}
												}
												catch (Exception ex)
												{
													Debug.WriteLine("磁磚配置系統作業" + ex.Message);
												}
											}
											else
											{
												Floor floor = element as Floor;
												bool flag9 = floor != null;
												if (flag9)
												{
													try
													{
														FloorType floorType = floor.FloorType;
														CompoundStructure compoundStructure2 = floorType.GetCompoundStructure();
														double thicknessFeet2 = (compoundStructure2 != null && compoundStructure2.LayerCount > 0) ? compoundStructure2.GetLayerWidth(0) : 0.03280839895013123;
														ElementId orCreateTileFloorType = geometryGenerator.GetOrCreateTileFloorType(doc, thicknessFeet2, id, name);
														bool flag10 = orCreateTileFloorType != ElementId.InvalidElementId;
														if (flag10)
														{
															floor.FloorType = (doc.GetElement(orCreateTileFloorType) as FloorType);
															this.SetSharedParameter(floor, "Tile_Material", name);
															num3++;
														}
													}
													catch (Exception ex2)
													{
														Debug.WriteLine("磁磚配置系統作業" + ex2.Message);
													}
												}
												else
												{
													DirectShape directShape = element as DirectShape;
													bool flag11 = directShape != null;
													if (flag11)
													{
														List<GeometryObject> list3 = new List<GeometryObject>();
														Options options = new Options
														{
															DetailLevel = ViewDetailLevel.Fine,
															ComputeReferences = true
														};
														GeometryElement geometryElement = directShape.get_Geometry(options);
														bool flag12 = geometryElement != null;
														if (flag12)
														{
															foreach (GeometryObject current2 in geometryElement)
															{
																Solid solid = current2 as Solid;
																bool flag13 = solid != null && solid.Volume > 1E-06;
																if (flag13)
																{
																	TessellatedShapeBuilder tessellatedShapeBuilder = new TessellatedShapeBuilder();
																	tessellatedShapeBuilder.OpenConnectedFaceSet(true);
																	foreach (Face face in solid.Faces)
																	{
																		Mesh mesh = face.Triangulate();
																		bool flag14 = mesh == null;
																		if (!flag14)
																		{
																			for (int j = 0; j < mesh.NumTriangles; j++)
																			{
																				MeshTriangle meshTriangle = mesh.get_Triangle(j);
																				XYZ item = mesh.Vertices[(int)meshTriangle.get_Index(0)];
																				XYZ item2 = mesh.Vertices[(int)meshTriangle.get_Index(1)];
																				XYZ item3 = mesh.Vertices[(int)meshTriangle.get_Index(2)];
																				List<XYZ> outerLoopVertices = new List<XYZ>
																				{
																					item,
																					item2,
																					item3
																				};
																				tessellatedShapeBuilder.AddFace(new TessellatedFace(outerLoopVertices, id));
																			}
																		}
																	}
																	tessellatedShapeBuilder.CloseConnectedFaceSet();
																	tessellatedShapeBuilder.Build();
																	TessellatedShapeBuilderResult buildResult = tessellatedShapeBuilder.GetBuildResult();
																	list3.AddRange(buildResult.GetGeometricalObjects());
																}
															}
														}
														bool flag15 = list3.Count > 0;
														if (flag15)
														{
															directShape.SetShape(list3);
															this.SetSharedParameter(directShape, "Tile_Material", name);
															num3++;
														}
													}
												}
											}
										}
									}
									transaction.Commit();
								}
								uidoc.RefreshActiveView();
								this.ViewModel.SetStatus(string.Format("更新成功，變更材質為 [{1}] 的 3D 磁磚共 {0} 塊。", num3, name));
							}
						}
					}
				}
			}
			catch (Exception ex3)
			{
				TaskDialog.Show("磁磚配置系統作業", ex3.ToString());
				this.ViewModel.SetStatus("磁磚配置系統作業" + ex3.Message);
			}
		}
		private static List<Curve> _patOnlySplitCurves = new List<Curve>();
		private static Plane _patOnlySketchPlane = null;
		private static Element _patOnlyHostElement = null;
		private static List<ElementId> _patOnlyPreviewLineIds = new List<ElementId>();
		private static List<ElementId> _patOnlyBoundaryPreviewLineIds = new List<ElementId>();
		private static TilePatternParams _patOnlyTileParams = null;
		private static string _patOnlyMaterialName = string.Empty;
		private static bool _tileLayoutControlIsSet = false;
		private static XYZ _tileLayoutControlOrigin = null;
		private static XYZ _tileLayoutControlUDir = null;
		private static List<TileLayoutPreviewSet> _tileLayoutPreviewSets = new List<TileLayoutPreviewSet>();
		private static List<ElementId> _tileLayoutControlMarkerIds = new List<ElementId>();
		private static ElementId _tileLayoutControlDirectionMarkerId = ElementId.InvalidElementId;
		private static List<TileSavedFaceReference> _savedTileLayoutFaceReferences = new List<TileSavedFaceReference>();
		private static Dictionary<string, TileLayoutControlState> _tileLayoutControlStates = new Dictionary<string, TileLayoutControlState>(StringComparer.OrdinalIgnoreCase);
		private static string _currentTileLayoutSpaceLabel = "未指定空間";
		private static ElementId _currentTileLayoutRoomId = ElementId.InvalidElementId;

		private class TileLayoutPreviewSet
		{
			public ElementId HostId { get; set; } = ElementId.InvalidElementId;
			public Plane SketchPlane { get; set; }
			public TilePatternParams TileParams { get; set; }
			public string MaterialName { get; set; } = string.Empty;
			public TileBoundaryJoinMode JoinMode { get; set; } = TileBoundaryJoinMode.IndependentToBoundary;
			public string SpaceLabel { get; set; } = "未指定空間";
			public ElementId RoomId { get; set; } = ElementId.InvalidElementId;
			public string SpaceKey { get; set; } = string.Empty;
			public List<ElementId> GridLineIds { get; } = new List<ElementId>();
			public List<ElementId> BoundaryLineIds { get; } = new List<ElementId>();
		}

		private class TileSavedFaceReference
		{
			public string StableReference { get; set; } = string.Empty;
			public ElementId ElementId { get; set; } = ElementId.InvalidElementId;
			public string SpaceLabel { get; set; } = "未指定空間";
			public ElementId RoomId { get; set; } = ElementId.InvalidElementId;
			public string SpaceKey { get; set; } = string.Empty;
		}

		private class TileLayoutControlState
		{
			public string SpaceKey { get; set; } = string.Empty;
			public string SpaceLabel { get; set; } = "未指定空間";
			public ElementId RoomId { get; set; } = ElementId.InvalidElementId;
			public XYZ Origin { get; set; }
			public XYZ UDir { get; set; }
			public bool IsSet { get; set; }
			public List<ElementId> MarkerIds { get; } = new List<ElementId>();
			public ElementId DirectionMarkerId { get; set; } = ElementId.InvalidElementId;
		}

		private class PatLineSeed
		{
			public Line Line { get; set; }
			public double Pitch { get; set; }
			public FillGrid Grid { get; set; }
			public int Ordinal { get; set; }
		}

		private class PartsTileQuantitySummary
		{
			public int Total { get; set; }
			public int Full { get; set; }
			public int HalfCut { get; set; }
			public int UnderHalfCut { get; set; }
			public int Cut { get; set; }
			public double AreaSqm { get; set; }
			public double FullTileAreaSqm { get; set; }
			public Dictionary<string, int> SurfaceTotals { get; } = new Dictionary<string, int>();
		}

		private class TileWasteSummary
		{
			public double NetQty { get; set; }
			public double PurchaseQty { get; set; }
			public double WasteQty { get; set; }
			public double WasteRate { get; set; }
		}

		private class PatPattern2DExportContext : IExportContext2D
		{
			private readonly ElementId _targetElementId;
			private bool _insideTargetElement;
			public List<Line> Lines { get; } = new List<Line>();

			public PatPattern2DExportContext(ElementId targetElementId)
			{
				_targetElementId = targetElementId;
			}

			public bool Start() { return true; }
			public void Finish() { }
			public bool IsCanceled() { return false; }
			public RenderNodeAction OnViewBegin(ViewNode node) { return RenderNodeAction.Proceed; }
			public void OnViewEnd(ElementId elementId) { }
			public RenderNodeAction OnElementBegin(ElementId elementId)
			{
				_insideTargetElement = elementId == _targetElementId;
				return _insideTargetElement ? RenderNodeAction.Proceed : RenderNodeAction.Skip;
			}
			public void OnElementEnd(ElementId elementId)
			{
				if (elementId == _targetElementId) _insideTargetElement = false;
			}
			public RenderNodeAction OnElementBegin2D(ElementNode node)
			{
				_insideTargetElement = node != null && node.ElementId == _targetElementId;
				return _insideTargetElement ? RenderNodeAction.Proceed : RenderNodeAction.Skip;
			}
			public void OnElementEnd2D(ElementNode node)
			{
				if (node != null && node.ElementId == _targetElementId) _insideTargetElement = false;
			}
			public RenderNodeAction OnCurve(CurveNode node)
			{
				if (!_insideTargetElement || node == null) return RenderNodeAction.Skip;
				try
				{
					Line line = node.GetCurve() as Line;
					if (line != null && line.IsBound)
					{
						Lines.Add(Line.CreateBound(line.GetEndPoint(0), line.GetEndPoint(1)));
					}
				}
				catch { }
				return RenderNodeAction.Skip;
			}
			public RenderNodeAction OnPolyline(PolylineNode node)
			{
				if (!_insideTargetElement || node == null) return RenderNodeAction.Skip;
				try
				{
					PolyLine polyline = node.GetPolyline();
					IList<XYZ> points = polyline == null ? null : polyline.GetCoordinates();
					if (points != null)
					{
						for (int i = 0; i < points.Count - 1; i++)
						{
							if (points[i].DistanceTo(points[i + 1]) > 0.001)
							{
								Lines.Add(Line.CreateBound(points[i], points[i + 1]));
							}
						}
					}
				}
				catch { }
				return RenderNodeAction.Skip;
			}
			public void OnLineSegment(LineSegment segment)
			{
				if (!_insideTargetElement || segment == null) return;
				try
				{
					if (segment.StartPoint.DistanceTo(segment.EndPoint) > 0.001)
					{
						Lines.Add(Line.CreateBound(segment.StartPoint, segment.EndPoint));
					}
				}
				catch { }
			}
			public void OnPolylineSegments(PolylineSegments segments) { }
			public void OnText(TextNode node) { }
			public RenderNodeAction OnInstanceBegin(InstanceNode node) { return RenderNodeAction.Proceed; }
			public void OnInstanceEnd(InstanceNode node) { }
			public RenderNodeAction OnLinkBegin(LinkNode node) { return RenderNodeAction.Skip; }
			public void OnLinkEnd(LinkNode node) { }
			public RenderNodeAction OnFaceBegin(FaceNode node) { return RenderNodeAction.Proceed; }
			public void OnFaceEnd(FaceNode node) { }
			public RenderNodeAction OnFaceEdge2D(FaceEdgeNode node) { return RenderNodeAction.Skip; }
			public RenderNodeAction OnFaceSilhouette2D(FaceSilhouetteNode node) { return RenderNodeAction.Skip; }
			public void OnRPC(RPCNode node) { }
			public void OnLight(LightNode node) { }
			public void OnMaterial(MaterialNode node) { }
			public void OnPolymesh(PolymeshTopology node) { }
		}

		private Material GetSelectedFaceMaterial(Document doc, Element hostElement, PlanarFace face)
		{
			if (doc == null || hostElement == null || face == null) return null;

			try
			{
				ElementId paintedMaterialId = doc.GetPaintedMaterial(hostElement.Id, face);
				if (paintedMaterialId != ElementId.InvalidElementId)
				{
					Material paintedMaterial = doc.GetElement(paintedMaterialId) as Material;
					if (paintedMaterial != null) return paintedMaterial;
				}
			}
			catch { }

			ElementId materialId = face.MaterialElementId;
			if (materialId == ElementId.InvalidElementId)
			{
				materialId = this.GetHostFinishMaterial(hostElement);
			}

			return materialId == ElementId.InvalidElementId ? null : doc.GetElement(materialId) as Material;
		}

		private FillPatternElement GetSelectedFaceSurfacePattern(Document doc, Element hostElement, PlanarFace face)
		{
			Material material = this.GetSelectedFaceMaterial(doc, hostElement, face);
			if (material == null || material.SurfaceForegroundPatternId == ElementId.InvalidElementId) return null;
			return doc.GetElement(material.SurfaceForegroundPatternId) as FillPatternElement;
		}

		private ICollection<ElementId> RecreatePatOnlyHostParts(Document doc, Element hostElement)
		{
			if (doc == null || hostElement == null) return new List<ElementId>();
			hostElement = this.ResolvePatOnlyPartsHost(doc, hostElement);
			if (hostElement == null) return new List<ElementId>();

			if (PartUtils.HasAssociatedParts(doc, hostElement.Id))
			{
				Element maker = PartUtils.GetAssociatedPartMaker(doc, hostElement.Id);
				if (maker != null)
				{
					doc.Delete(maker.Id);
					doc.Regenerate();
				}
			}

			ICollection<ElementId> hostIds = new List<ElementId> { hostElement.Id };
			if (PartUtils.AreElementsValidForCreateParts(doc, hostIds))
			{
				PartUtils.CreateParts(doc, hostIds);
				doc.Regenerate();
			}

			return this.GetPartsValidForDivide(doc, hostElement);
		}

		private ICollection<ElementId> EnsurePatOnlyHostParts(Document doc, Element hostElement)
		{
			if (doc == null || hostElement == null) return new List<ElementId>();
			hostElement = this.ResolvePatOnlyPartsHost(doc, hostElement);
			if (hostElement == null) return new List<ElementId>();

			if (!PartUtils.HasAssociatedParts(doc, hostElement.Id))
			{
				ICollection<ElementId> hostIds = new List<ElementId> { hostElement.Id };
				if (!PartUtils.AreElementsValidForCreateParts(doc, hostIds))
				{
					return new List<ElementId>();
				}

				PartUtils.CreateParts(doc, hostIds);
				doc.Regenerate();
			}

			return this.GetPartsValidForDivide(doc, hostElement);
		}

		private Element ResolvePatOnlyPartsHost(Document doc, Element element)
		{
			Element current = element;
			HashSet<ElementId> visited = new HashSet<ElementId>();

			while (current is Part part)
			{
				if (!visited.Add(current.Id)) break;

				ICollection<LinkElementId> sources = part.GetSourceElementIds();
				LinkElementId source = sources == null ? null : sources.FirstOrDefault();
				if (source == null || source.HostElementId == ElementId.InvalidElementId) break;

				Element next = doc.GetElement(source.HostElementId);
				if (next == null) break;
				current = next;
			}

			return current;
		}

		private ICollection<ElementId> GetPartsValidForDivide(Document doc, Element hostElement)
		{
			if (doc == null || hostElement == null || !PartUtils.HasAssociatedParts(doc, hostElement.Id))
			{
				return new List<ElementId>();
			}

			bool[] includePartsWithAssociatedPartsOptions = new[] { false, true };
			bool[] includeAllChildrenOptions = new[] { true, false };

			foreach (bool includePartsWithAssociatedParts in includePartsWithAssociatedPartsOptions)
			{
				foreach (bool includeAllChildren in includeAllChildrenOptions)
				{
					ICollection<ElementId> candidate = PartUtils.GetAssociatedParts(
						doc,
						hostElement.Id,
						includePartsWithAssociatedParts,
						includeAllChildren);

					if (candidate != null && candidate.Count > 0 && PartUtils.ArePartsValidForDivide(doc, candidate))
					{
						return candidate;
					}
				}
			}

			return PartUtils.GetAssociatedParts(doc, hostElement.Id, false, true) ?? new List<ElementId>();
		}

		private void EnablePatOnlyPartsViews(Document doc, View activeView)
		{
			List<View> views = new List<View>();
			if (activeView != null && !activeView.IsTemplate) views.Add(activeView);

			foreach (View3D view3D in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>())
			{
				if (view3D == null || view3D.IsTemplate) continue;
				if (!views.Any(v => v.Id == view3D.Id)) views.Add(view3D);
			}

			ElementId partsCategoryId = new ElementId(BuiltInCategory.OST_Parts);
			foreach (View view in views)
			{
				try { view.PartsVisibility = PartsVisibility.ShowPartsOnly; } catch { }
				try
				{
					if (view.CanCategoryBeHidden(partsCategoryId)) view.SetCategoryHidden(partsCategoryId, false);
				}
				catch { }
			}
		}

		private XYZ ProjectPointToPlanarFace(XYZ point, PlanarFace face)
		{
			XYZ normal = face.FaceNormal.Normalize();
			double distance = (point - face.Origin).DotProduct(normal);
			return point - distance * normal;
		}

		private List<XYZ> GetPatternDimensionCandidateDirections(PlanarFace face, View view)
		{
			List<XYZ> directions = new List<XYZ>();
			XYZ normal = face.FaceNormal.Normalize();
			this.AddPatternDimensionDirection(directions, face.XVector, normal);
			this.AddPatternDimensionDirection(directions, face.YVector, normal);
			if (view != null)
			{
				this.AddPatternDimensionDirection(directions, view.RightDirection, normal);
				this.AddPatternDimensionDirection(directions, view.UpDirection, normal);
			}
			this.AddPatternDimensionDirection(directions, face.XVector + face.YVector, normal);
			this.AddPatternDimensionDirection(directions, face.XVector - face.YVector, normal);
			return directions;
		}

		private void AddPatternDimensionDirection(List<XYZ> directions, XYZ direction, XYZ normal)
		{
			if (directions == null || direction == null) return;
			XYZ projected = direction - direction.DotProduct(normal) * normal;
			if (projected.GetLength() < 0.0001) return;
			projected = projected.Normalize();
			foreach (XYZ existing in directions)
			{
				if (Math.Abs(existing.DotProduct(projected)) > 0.999) return;
			}
			directions.Add(projected);
		}

		private int EstimatePatReferenceSampleCount(PlanarFace face, IList<FillGrid> grids)
		{
			double maxSpan = this.GetFaceMaxSpan(face);
			double smallestPitch = double.MaxValue;
			if (grids != null)
			{
				foreach (FillGrid grid in grids)
				{
					if (grid == null) continue;
					double offset = Math.Abs(grid.Offset);
					if (offset > 0.001 && offset < smallestPitch) smallestPitch = offset;
				}
			}
			if (smallestPitch < double.MaxValue && maxSpan > 0.0)
			{
				return Math.Max(12, Math.Min(300, (int)Math.Ceiling(maxSpan / smallestPitch) + 12));
			}
			return 80;
		}

		private double GetFaceMaxSpan(PlanarFace face)
		{
			List<XYZ> points = this.GetProjectedFacePoints(face);
			double maxSpan = 0.0;
			for (int i = 0; i < points.Count; i++)
			{
				for (int j = i + 1; j < points.Count; j++)
				{
					double distance = points[i].DistanceTo(points[j]);
					if (distance > maxSpan) maxSpan = distance;
				}
			}
			return maxSpan;
		}

		private List<XYZ> GetProjectedFacePoints(PlanarFace face)
		{
			List<XYZ> points = new List<XYZ>();
			if (face == null) return points;
			foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
			{
				foreach (Curve curve in loop)
				{
					foreach (XYZ point in curve.Tessellate())
					{
						points.Add(this.ProjectPointToPlanarFace(point, face));
					}
				}
			}
			return points;
		}

		private List<Line> BuildPatOnlySplitLines(Document doc, View view, Reference faceReference, PlanarFace face, FillPatternElement patternElement, out string diagnostic)
		{
			diagnostic = string.Empty;
			List<Line> lines = new List<Line>();
			if (doc == null || view == null || faceReference == null || face == null || patternElement == null)
			{
				diagnostic = "No selected face Model PAT was found.";
				return lines;
			}

			FillPattern pattern = patternElement.GetFillPattern();
			if (pattern == null || pattern.IsSolidFill || pattern.Target == FillPatternTarget.Drafting)
			{
				diagnostic = "Selected face pattern is not a Model PAT.";
				return lines;
			}

			IList<FillGrid> grids = pattern.GetFillGrids();
			int gridCount = grids == null ? 0 : grids.Count;
			if (gridCount == 0)
			{
				diagnostic = "Selected Model PAT has no hatch grids.";
				return lines;
			}

			string exportDiagnostic;
			List<Line> exportedLines = this.TryExportDisplayedPatLinesFrom2DView(doc, faceReference.ElementId, face, grids, out exportDiagnostic);
			if (exportedLines.Count > 0)
			{
				return exportedLines;
			}

			string referenceDiagnostic;
			List<Line> referenceLines = this.TryReadPatLinesFromStableReferences(doc, faceReference, face, grids, out referenceDiagnostic);
			if (referenceLines.Count > 0)
			{
				return referenceLines;
			}

			diagnostic = string.Join("\n", new[]
			{
				string.IsNullOrEmpty(exportDiagnostic) ? "Revit 2D export did not return displayed PAT lines for the selected face." : exportDiagnostic,
				string.IsNullOrEmpty(referenceDiagnostic) ? "Stable PAT references did not return usable lines." : referenceDiagnostic
			}.Where(s => !string.IsNullOrEmpty(s)));
			return lines;

		}

		private List<Line> TryReadPatLinesFromStableReferences(Document doc, Reference faceReference, PlanarFace face, IList<FillGrid> grids, out string diagnostic)
		{
			diagnostic = string.Empty;
			List<Line> lines = new List<Line>();
			if (doc == null || faceReference == null || face == null || grids == null || grids.Count == 0)
			{
				diagnostic = "Missing selected face PAT references.";
				return lines;
			}

			string stableFace;
			try
			{
				stableFace = faceReference.ConvertToStableRepresentation(doc);
			}
			catch (Exception ex)
			{
				diagnostic = "Cannot read selected face stable reference: " + ex.Message;
				return lines;
			}

			List<string> failures = new List<string>();
			int gridCount = grids.Count;
			int sampleCount = this.EstimatePatReferenceSampleCount(face, grids);
			TransactionGroup group = new TransactionGroup(doc, "Read selected face PAT references");
			bool groupStarted = false;
			try
			{
				group.Start();
				groupStarted = true;
				using (Transaction t = new Transaction(doc, "Temporary PAT dimensions"))
				{
					t.Start();
					string viewDiagnostic;
					View dimensionView = this.CreateTemporaryPatReadView(doc, face, out viewDiagnostic);
					if (dimensionView == null)
					{
						diagnostic = viewDiagnostic;
						t.RollBack();
						return lines;
					}

					doc.Regenerate();
					XYZ dimensionAnchor = this.GetFaceCenter(face);
					List<XYZ> dimensionDirections = this.GetPatternDimensionCandidateDirections(face, dimensionView);
					for (int gridIndex = 0; gridIndex < gridCount; gridIndex++)
					{
						foreach (Tuple<int, int> indexPattern in this.GetPatReferenceIndexPatterns(gridIndex, gridCount))
						{
							int beforePatternCount = lines.Count;
							for (int sample = 0; sample < sampleCount; sample++)
							{
								int firstIndex = indexPattern.Item1 + (sample * indexPattern.Item2);
								int secondIndex = indexPattern.Item1 + ((sample + 1) * indexPattern.Item2);
								ReferenceArray refs = new ReferenceArray();
								try
								{
									Reference firstRef = Reference.ParseFromStableRepresentation(doc, stableFace + "/" + firstIndex.ToString());
									Reference secondRef = Reference.ParseFromStableRepresentation(doc, stableFace + "/" + secondIndex.ToString());
									if (firstRef != null) refs.Append(firstRef);
									if (secondRef != null) refs.Append(secondRef);
								}
								catch
								{
									if (sample > 8) break;
									continue;
								}

								if (refs.Size < 2) continue;
								foreach (XYZ dimDirection in dimensionDirections)
								{
									try
									{
										Dimension dim = doc.Create.NewDimension(dimensionView, Line.CreateBound(dimensionAnchor, dimensionAnchor + dimDirection), refs);
										if (dim == null) continue;
										ElementTransformUtils.MoveElement(doc, dim.Id, dimDirection * 0.1);
										doc.Regenerate();

										Line dimLine = dim.Curve as Line;
										if (dimLine == null) continue;

										XYZ hatchPerp = dimLine.Direction.Normalize();
										XYZ hatchDir = face.FaceNormal.Normalize().CrossProduct(hatchPerp);
										if (hatchDir.GetLength() < 0.0001) continue;
										hatchDir = hatchDir.Normalize();

										double pitch = Math.Abs(Convert.ToDouble(dim.Value));
										if (pitch < 0.001 && grids[gridIndex] != null) pitch = Math.Abs(grids[gridIndex].Offset);
										if (pitch < 0.001) continue;

										XYZ originOnFace = this.ProjectPointToPlanarFace(dim.Origin, face);
										XYZ firstPoint = this.ProjectPointToPlanarFace(originOnFace - hatchPerp * (pitch * 0.5), face);
										XYZ secondPoint = this.ProjectPointToPlanarFace(originOnFace + hatchPerp * (pitch * 0.5), face);

										foreach (Line line in this.CreateFaceSpanPatternLines(face, firstPoint, hatchDir, grids[gridIndex], sample))
										{
											this.TryAddUniquePatLine(lines, line, face.FaceNormal);
										}
										foreach (Line line in this.CreateFaceSpanPatternLines(face, secondPoint, hatchDir, grids[gridIndex], sample + 1))
										{
											this.TryAddUniquePatLine(lines, line, face.FaceNormal);
										}
										break;
									}
									catch (Exception ex)
									{
										if (failures.Count < 5) failures.Add(ex.Message);
									}
								}
							}

							if (lines.Count > beforePatternCount) break;
						}
					}
					t.Commit();
				}
			}
			catch (Exception ex)
			{
				diagnostic = ex.Message;
			}
			finally
			{
				if (groupStarted)
				{
					try { group.RollBack(); } catch { }
				}
			}

			if (lines.Count == 0 && string.IsNullOrEmpty(diagnostic))
			{
				diagnostic = failures.Count == 0 ? "No hatch references could be converted to PAT curves." : string.Join("\n", failures);
			}
			return lines;
		}

		private List<Line> TryExportDisplayedPatLinesFrom2DView(Document doc, ElementId hostElementId, PlanarFace face, IList<FillGrid> grids, out string diagnostic)
		{
			diagnostic = string.Empty;
			List<Line> result = new List<Line>();
			if (doc == null || hostElementId == ElementId.InvalidElementId || face == null) return result;

			TransactionGroup group = new TransactionGroup(doc, "Export displayed PAT lines");
			bool groupStarted = false;
			try
			{
				group.Start();
				groupStarted = true;
				View exportView;
				using (Transaction t = new Transaction(doc, "Create PAT export view"))
				{
					t.Start();
					string viewDiagnostic;
					exportView = this.CreateTemporaryPatReadView(doc, face, out viewDiagnostic);
					if (exportView == null)
					{
						diagnostic = viewDiagnostic;
						t.RollBack();
						return result;
					}
					try { exportView.DetailLevel = ViewDetailLevel.Fine; } catch { }
					try { exportView.DisplayStyle = DisplayStyle.HLR; } catch { }
					doc.Regenerate();
					t.Commit();
				}

				PatPattern2DExportContext context = new PatPattern2DExportContext(hostElementId);
				using (CustomExporter exporter = new CustomExporter(doc, context))
				{
					exporter.Export2DGeometricObjectsIncludingPatternLines = true;
					exporter.Export2DIncludingAnnotationObjects = false;
					exporter.Export2DForceDisplayStyle = DisplayStyle.HLR;
					exporter.IncludeGeometricObjects = true;
					exporter.ShouldStopOnError = false;
					exporter.Export(exportView);
				}

				foreach (Line rawLine in context.Lines)
				{
					Line line = this.ProjectAndFilterExportedPatLine(face, rawLine, grids);
					if (line != null)
					{
						this.TryAddUniquePatLine(result, line, face.FaceNormal);
					}
				}
			}
			catch (Exception ex)
			{
				diagnostic = ex.Message;
			}
			finally
			{
				if (groupStarted)
				{
					try { group.RollBack(); } catch { }
				}
			}

			return result;
		}

		private Line ProjectAndFilterExportedPatLine(PlanarFace face, Line rawLine, IList<FillGrid> grids)
		{
			if (face == null || rawLine == null) return null;
			XYZ p0 = this.ProjectPointToPlanarFace(rawLine.GetEndPoint(0), face);
			XYZ p1 = this.ProjectPointToPlanarFace(rawLine.GetEndPoint(1), face);
			if (p0.DistanceTo(p1) < 0.005) return null;

			Line projected;
			try { projected = Line.CreateBound(p0, p1); }
			catch { return null; }

			if (this.IsLineOnFaceBoundary(face, projected)) return null;
			XYZ midpoint = (p0 + p1) * 0.5;
			if (!this.IsPointInsidePatFace(face, midpoint)) return null;
			if (!this.DoesLineMatchFillGridDirection(face, projected, grids)) return null;
			return projected;
		}

		private bool DoesLineMatchFillGridDirection(PlanarFace face, Line line, IList<FillGrid> grids)
		{
			if (face == null || line == null) return false;
			if (grids == null || grids.Count == 0) return true;

			XYZ lineDir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
			XYZ uDir = face.XVector.Normalize();
			XYZ vDir = face.YVector.Normalize();
			foreach (FillGrid grid in grids)
			{
				if (grid == null) continue;
				double angle = grid.Angle;
				XYZ gridDir = (Math.Cos(angle) * uDir + Math.Sin(angle) * vDir);
				if (gridDir.GetLength() < 0.0001) continue;
				gridDir = gridDir.Normalize();
				if (Math.Abs(gridDir.DotProduct(lineDir)) > 0.995) return true;
			}
			return false;
		}

		private List<Tuple<int, int>> GetPatReferenceIndexPatterns(int gridIndex, int gridCount)
		{
			List<Tuple<int, int>> patterns = new List<Tuple<int, int>>();
			int start = 1 + gridIndex;
			this.AddPatReferenceIndexPattern(patterns, start, Math.Max(1, gridCount));
			this.AddPatReferenceIndexPattern(patterns, start, 2);
			this.AddPatReferenceIndexPattern(patterns, start, Math.Max(1, gridCount * 2));
			this.AddPatReferenceIndexPattern(patterns, start, 1);
			return patterns;
		}

		private void AddPatReferenceIndexPattern(List<Tuple<int, int>> patterns, int start, int stride)
		{
			if (patterns == null || stride < 1) return;
			foreach (Tuple<int, int> existing in patterns)
			{
				if (existing.Item1 == start && existing.Item2 == stride) return;
			}
			patterns.Add(Tuple.Create(start, stride));
		}

		private void CompletePatOnlyGridFromSeeds(PlanarFace face, IList<PatLineSeed> seeds, List<Line> lines)
		{
			if (face == null || seeds == null || lines == null) return;
			double faceSpan = this.GetFaceMaxSpan(face);
			if (faceSpan < 0.001) return;

			XYZ normal = face.FaceNormal.Normalize();
			foreach (PatLineSeed seed in seeds.ToList())
			{
				if (seed == null || seed.Line == null) continue;
				double pitch = seed.Pitch;
				if (seed.Grid != null && Math.Abs(seed.Grid.Offset) > 0.001)
				{
					pitch = Math.Abs(seed.Grid.Offset);
				}
				if (pitch < 0.001) continue;

				XYZ direction = (seed.Line.GetEndPoint(1) - seed.Line.GetEndPoint(0)).Normalize();
				XYZ perp = normal.CrossProduct(direction);
				if (perp.GetLength() < 0.0001) continue;
				perp = perp.Normalize();

				XYZ anchor = this.ProjectPointToPlanarFace((seed.Line.GetEndPoint(0) + seed.Line.GetEndPoint(1)) * 0.5, face);
				int steps = Math.Min(500, (int)Math.Ceiling((faceSpan + pitch * 4.0) / pitch) + 4);
				for (int i = -steps; i <= steps; i++)
				{
					XYZ point = anchor + perp * (pitch * i);
					int ordinal = seed.Ordinal + i;
					foreach (Line line in this.CreateFaceSpanPatternLines(face, point, direction, seed.Grid, ordinal))
					{
						this.TryAddUniquePatLine(lines, line, normal);
					}
				}
			}
		}

		private XYZ GetFaceCenter(PlanarFace face)
		{
			List<XYZ> points = this.GetProjectedFacePoints(face);
			if (points.Count == 0) return face.Origin;
			return points.Aggregate(XYZ.Zero, (sum, point) => sum + point) / points.Count;
		}

		private View CreateTemporaryPatReadView(Document doc, PlanarFace face, out string diagnostic)
		{
			diagnostic = string.Empty;
			try
			{
				ElementId sectionTypeId = new FilteredElementCollector(doc)
					.OfClass(typeof(ViewFamilyType))
					.Cast<ViewFamilyType>()
					.FirstOrDefault(t => t.ViewFamily == ViewFamily.Section)?.Id ?? ElementId.InvalidElementId;

				if (sectionTypeId == ElementId.InvalidElementId)
				{
					diagnostic = "找不到可建立臨時剖面視圖的 Section ViewFamilyType。";
					return null;
				}

				List<XYZ> points = this.GetProjectedFacePoints(face);
				if (points.Count == 0)
				{
					diagnostic = "無法取得選取面的邊界點，不能建立 PAT 讀取視圖。";
					return null;
				}

				XYZ normal = face.FaceNormal.Normalize();
				XYZ uDir = face.XVector.Normalize();
				XYZ vDir = face.YVector.Normalize();
				if (uDir.CrossProduct(vDir).DotProduct(normal) < 0)
				{
					uDir = uDir.Negate();
				}

				XYZ origin = points.Aggregate(XYZ.Zero, (sum, point) => sum + point) / points.Count;
				double minU = double.MaxValue;
				double maxU = double.MinValue;
				double minV = double.MaxValue;
				double maxV = double.MinValue;
				foreach (XYZ point in points)
				{
					XYZ rel = point - origin;
					double u = rel.DotProduct(uDir);
					double v = rel.DotProduct(vDir);
					minU = Math.Min(minU, u);
					maxU = Math.Max(maxU, u);
					minV = Math.Min(minV, v);
					maxV = Math.Max(maxV, v);
				}

				double pad = 2.0;
				double depth = 2.0;
				Transform transform = Transform.Identity;
				transform.Origin = origin;
				transform.BasisX = uDir;
				transform.BasisY = vDir;
				transform.BasisZ = normal;

				BoundingBoxXYZ box = new BoundingBoxXYZ
				{
					Transform = transform,
					Min = new XYZ(minU - pad, minV - pad, -depth),
					Max = new XYZ(maxU + pad, maxV + pad, depth)
				};

				ViewSection section = ViewSection.CreateSection(doc, sectionTypeId, box);
				if (section == null)
				{
					diagnostic = "Revit 無法建立臨時 PAT 讀取剖面視圖。";
					return null;
				}

				section.Name = "TilePAT_Temporary_Read_View_" + Guid.NewGuid().ToString("N").Substring(0, 8);
				section.CropBoxActive = true;
				section.CropBoxVisible = false;
				return section;
			}
			catch (Exception ex)
			{
				diagnostic = "建立臨時 PAT 讀取剖面視圖失敗：" + ex.Message;
				return null;
			}
		}

		private List<Line> CreateFaceSpanPatternLines(PlanarFace face, XYZ pointOnPatternLine, XYZ patternDirection, FillGrid grid, int lineOrdinal)
		{
			List<Line> lines = new List<Line>();
			XYZ direction = patternDirection.Normalize();
			XYZ basePoint = this.ProjectPointToPlanarFace(pointOnPatternLine, face);
			List<Tuple<double, double>> intervals = this.GetLineIntervalsInsideFace(face, basePoint, direction);
			if (intervals.Count == 0) return lines;

			IList<double> segments = null;
			try { segments = grid == null ? null : grid.GetSegments(); } catch { }

			bool hasDash = segments != null && segments.Count > 1 && segments.Any(s => s > 0.001) && segments.Any(s => s < -0.001);
			if (!hasDash)
			{
				foreach (Tuple<double, double> interval in intervals)
				{
					Line line = this.TryCreatePatLine(basePoint, direction, interval.Item1, interval.Item2);
					if (line != null) lines.Add(line);
				}
				return lines;
			}

			double cycle = segments.Sum(s => Math.Abs(s));
			if (cycle < 0.001) return lines;
			double phase = 0.0;
			try { phase = (grid == null ? 0.0 : grid.Shift) * lineOrdinal; } catch { }

			foreach (Tuple<double, double> interval in intervals)
			{
				double start = Math.Min(interval.Item1, interval.Item2);
				double end = Math.Max(interval.Item1, interval.Item2);
				int startCycle = (int)Math.Floor((start - phase) / cycle) - 1;
				int endCycle = (int)Math.Ceiling((end - phase) / cycle) + 1;
				for (int cycleIndex = startCycle; cycleIndex <= endCycle; cycleIndex++)
				{
					double cursor = phase + cycleIndex * cycle;
					foreach (double segment in segments)
					{
						double length = Math.Abs(segment);
						if (length < 0.001) continue;
						double segmentStart = cursor;
						double segmentEnd = cursor + length;
						if (segment > 0.0)
						{
							Line line = this.TryCreatePatLine(basePoint, direction, Math.Max(start, segmentStart), Math.Min(end, segmentEnd));
							if (line != null) lines.Add(line);
						}
						cursor = segmentEnd;
					}
				}
			}
			return lines;
		}

		private Line TryCreatePatLine(XYZ basePoint, XYZ direction, double start, double end)
		{
			if (Math.Abs(end - start) < 0.005) return null;
			try { return Line.CreateBound(basePoint + start * direction, basePoint + end * direction); }
			catch { return null; }
		}

		private List<Tuple<double, double>> GetLineIntervalsInsideFace(PlanarFace face, XYZ basePoint, XYZ direction)
		{
			List<Tuple<double, double>> intervals = new List<Tuple<double, double>>();
			XYZ dir = direction.Normalize();
			XYZ normal = face.FaceNormal.Normalize();
			XYZ perp = normal.CrossProduct(dir);
			if (perp.GetLength() < 0.0001) return intervals;
			perp = perp.Normalize();

			List<double> cuts = new List<double>();
			const double tolerance = 0.0005;
			foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
			{
				foreach (Curve curve in loop)
				{
					List<XYZ> pts = curve.Tessellate().Select(p => this.ProjectPointToPlanarFace(p, face)).ToList();
					for (int i = 0; i < pts.Count - 1; i++)
					{
						XYZ p0 = pts[i];
						XYZ p1 = pts[i + 1];
						double a0 = (p0 - basePoint).DotProduct(dir);
						double a1 = (p1 - basePoint).DotProduct(dir);
						double d0 = (p0 - basePoint).DotProduct(perp);
						double d1 = (p1 - basePoint).DotProduct(perp);
						if (Math.Abs(d0) < tolerance) cuts.Add(a0);
						if (Math.Abs(d1) < tolerance) cuts.Add(a1);
						if (d0 * d1 < 0.0)
						{
							double fraction = Math.Abs(d0) / (Math.Abs(d0) + Math.Abs(d1));
							cuts.Add(a0 + fraction * (a1 - a0));
						}
						if (Math.Abs(d0) < tolerance && Math.Abs(d1) < tolerance)
						{
							cuts.Add(a0);
							cuts.Add(a1);
						}
					}
				}
			}

			List<double> uniqueCuts = cuts.OrderBy(v => v).Aggregate(new List<double>(), (acc, v) =>
			{
				if (acc.Count == 0 || Math.Abs(v - acc[acc.Count - 1]) > 0.001) acc.Add(v);
				return acc;
			});

			for (int i = 0; i < uniqueCuts.Count - 1; i++)
			{
				double start = uniqueCuts[i];
				double end = uniqueCuts[i + 1];
				if (end - start < 0.005) continue;
				if (this.IsPointInsidePatFace(face, basePoint + ((start + end) * 0.5) * dir))
				{
					intervals.Add(Tuple.Create(start, end));
				}
			}
			return intervals;
		}

		private bool IsPointInsidePatFace(PlanarFace face, XYZ point)
		{
			try
			{
				IntersectionResult projection = face.Project(point);
				return projection != null && face.IsInside(projection.UVPoint);
			}
			catch { return false; }
		}

		private bool TryAddUniquePatLine(List<Line> lines, Line candidate, XYZ faceNormal)
		{
			if (lines == null || candidate == null) return false;
			XYZ dir = (candidate.GetEndPoint(1) - candidate.GetEndPoint(0)).Normalize();
			XYZ mid = (candidate.GetEndPoint(0) + candidate.GetEndPoint(1)) * 0.5;
			XYZ perp = faceNormal.Normalize().CrossProduct(dir);
			if (perp.GetLength() < 0.0001) return false;
			perp = perp.Normalize();

			foreach (Line existing in lines)
			{
				XYZ existingDir = (existing.GetEndPoint(1) - existing.GetEndPoint(0)).Normalize();
				if (Math.Abs(existingDir.DotProduct(dir)) < 0.999) continue;
				XYZ existingMid = (existing.GetEndPoint(0) + existing.GetEndPoint(1)) * 0.5;
				if (Math.Abs((existingMid - mid).DotProduct(perp)) > 0.0005) continue;
				double existingLength = existing.GetEndPoint(0).DistanceTo(existing.GetEndPoint(1));
				double c0 = (candidate.GetEndPoint(0) - existing.GetEndPoint(0)).DotProduct(existingDir);
				double c1 = (candidate.GetEndPoint(1) - existing.GetEndPoint(0)).DotProduct(existingDir);
				if (Math.Min(existingLength, Math.Max(c0, c1)) - Math.Max(0.0, Math.Min(c0, c1)) > 0.0005) return false;
			}

			lines.Add(candidate);
			return true;
		}

		private bool TryPickPatFace(UIDocument uidoc, Document doc, out Reference reference, out Element hostElement, out PlanarFace face, string prompt)
		{
			reference = null;
			hostElement = null;
			face = null;
			try { reference = uidoc.Selection.PickObject(ObjectType.Face, prompt); }
			catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return false; }
			if (reference == null) return false;
			Element pickedElement = doc.GetElement(reference.ElementId);
			face = pickedElement == null ? null : pickedElement.GetGeometryObjectFromReference(reference) as PlanarFace;
			hostElement = this.ResolvePatOnlyPartsHost(doc, pickedElement);
			return hostElement != null && face != null;
		}

		private TilePatternParams ResolveSelectedTilePatternParams(Document doc, Element hostElement, PlanarFace face)
		{
			TilePatternParams p = TileDimensionDetector.DetectPatternParams(
				hostElement,
				doc,
				this.ViewModel.TileWidth,
				this.ViewModel.TileHeight,
				this.ViewModel.JointWidth,
				this.ViewModel.TileThickness);

			Material material = this.GetSelectedFaceMaterial(doc, hostElement, face);
			if (material != null)
			{
				TilePatternParams fromMaterial = this.ParseParamsFromMatName(material.Name);
				if (fromMaterial != null)
				{
					p.TileWidth = fromMaterial.TileWidth;
					p.TileHeight = fromMaterial.TileHeight;
					p.JointWidth = fromMaterial.JointWidth;
					p.Style = fromMaterial.Style;
				}
				else if (TileDimensionDetector.ParseDimensions(material.Name, out double w, out double h))
				{
					p.TileWidth = w;
					p.TileHeight = h;
				}

				try
				{
					FillPatternElement fillPatternElement = material.SurfaceForegroundPatternId == ElementId.InvalidElementId
						? null
						: doc.GetElement(material.SurfaceForegroundPatternId) as FillPatternElement;
					TilePatternParams fromPattern = fillPatternElement == null ? null : this.ParseParamsFromPatName(fillPatternElement.Name);
					if (fromPattern != null)
					{
						p.TileWidth = fromPattern.TileWidth;
						p.TileHeight = fromPattern.TileHeight;
						p.JointWidth = fromPattern.JointWidth;
						p.Style = fromPattern.Style;
					}
				}
				catch { }
			}

			this.ViewModel.TileWidth = p.TileWidth;
			this.ViewModel.TileHeight = p.TileHeight;
			this.ViewModel.JointWidth = p.JointWidth;
			this.ViewModel.TileThickness = p.Thickness;
			return p;
		}

		private bool TryGetLayoutDrivenCurves(UIDocument uidoc, Document doc, Reference reference, Element hostElement, PlanarFace face, out List<Curve> curves, out Plane sketchPlane, out string diagnostic)
		{
			curves = new List<Curve>();
			sketchPlane = null;
			diagnostic = string.Empty;
			if (doc == null || reference == null || hostElement == null || face == null)
			{
				diagnostic = "沒有可用的宿主面。";
				return false;
			}

			TilePatternParams p = this.ResolveSelectedTilePatternParams(doc, hostElement, face);
			SurfaceTileLayoutData layout = null;
			FaceDrivenGeometryAnalyzer analyzer = new FaceDrivenGeometryAnalyzer(doc, this.FindAny3DView(doc));
			Wall wall = hostElement as Wall;
			Floor floor = hostElement as Floor;

			if (wall != null)
			{
				double minZ = double.MaxValue;
				double maxZ = double.MinValue;
				foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
				{
					foreach (Curve curve in loop)
					{
						foreach (XYZ pt in curve.Tessellate())
						{
							minZ = Math.Min(minZ, pt.Z);
							maxZ = Math.Max(maxZ, pt.Z);
						}
					}
				}
				double startHeightMm = 0.0;
				double endHeightMm = Math.Max(1.0, (maxZ - minZ) * 304.8);
				WallSurfaceGeometry wallGeo = analyzer.BuildWallGeometryFromFace(wall, face, reference, startHeightMm, endHeightMm);
				if (wallGeo != null)
				{
					layout = new TileLayoutEngine().LayoutWall(wallGeo, null, p, "Parts", "Parts", 0, 0.0, 0.0);
				}
			}
			else if (floor != null)
			{
				FloorSurfaceGeometry floorGeo = analyzer.BuildFloorGeometryFromFace(floor, face, reference);
				if (floorGeo != null)
				{
					RoomLocalCoordinate localCoord = new RoomLocalCoordinate(floorGeo.Origin, floorGeo.XVector, floorGeo.YVector);
					layout = new TileLayoutEngine().LayoutFloor(floorGeo.BoundaryLoops, localCoord, p, "Parts", "Parts", hostElement.UniqueId, 0.0, 0.0);
				}
			}
			else
			{
				diagnostic = "目前只支援牆與樓板建立 Parts。";
				return false;
			}

			if (layout == null || layout.Tiles == null || layout.Tiles.Count == 0)
			{
				diagnostic = "無法由已確認的磁磚排版資料產生分割線。";
				return false;
			}

			List<Line> lines = this.BuildSplitLinesFromTileBoundaries(face, layout);
			if (lines.Count == 0)
			{
				diagnostic = "磁磚邊界沒有產生可用的 Parts 分割線。";
				return false;
			}

			curves.AddRange(lines.Cast<Curve>());
			sketchPlane = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
			diagnostic = string.Format("已由排版資料產生 {0} 條分割線，尺寸 {1:0.#}x{2:0.#} mm，縫 {3:0.#} mm。", lines.Count, p.TileWidth, p.TileHeight, p.JointWidth);
			return true;
		}

		private List<Line> BuildSplitLinesFromTileBoundaries(PlanarFace face, SurfaceTileLayoutData layout)
		{
			List<Line> lines = new List<Line>();
			foreach (TileData tile in layout.Tiles)
			{
				if (tile == null || tile.XYZBoundary == null || tile.XYZBoundary.Count < 3) continue;
				for (int i = 0; i < tile.XYZBoundary.Count; i++)
				{
					XYZ a = this.ProjectPointToPlanarFace(this.ToXyz(tile.XYZBoundary[i]), face);
					XYZ b = this.ProjectPointToPlanarFace(this.ToXyz(tile.XYZBoundary[(i + 1) % tile.XYZBoundary.Count]), face);
					if (a.DistanceTo(b) < 0.01) continue;
					Line line;
					try { line = Line.CreateBound(a, b); }
					catch { continue; }
					if (this.IsLineOnFaceBoundary(face, line)) continue;
					this.TryAddUniqueLayoutLine(lines, line);
				}
			}
			return lines;
		}

		private XYZ ToXyz(XYZPoint point)
		{
			return new XYZ(point.X, point.Y, point.Z);
		}

		private bool TryAddUniqueLayoutLine(List<Line> lines, Line candidate)
		{
			XYZ c0 = candidate.GetEndPoint(0);
			XYZ c1 = candidate.GetEndPoint(1);
			foreach (Line existing in lines)
			{
				XYZ e0 = existing.GetEndPoint(0);
				XYZ e1 = existing.GetEndPoint(1);
				if ((c0.DistanceTo(e0) < 0.001 && c1.DistanceTo(e1) < 0.001) ||
					(c0.DistanceTo(e1) < 0.001 && c1.DistanceTo(e0) < 0.001))
				{
					return false;
				}
			}
			lines.Add(candidate);
			return true;
		}

		private bool IsLineOnFaceBoundary(PlanarFace face, Line line)
		{
			XYZ midpoint = (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;
			foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
			{
				foreach (Curve edge in loop)
				{
					IList<XYZ> points = edge.Tessellate();
					for (int i = 0; i < points.Count - 1; i++)
					{
						if (this.DistancePointToSegment(midpoint, points[i], points[i + 1]) < 0.003)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		private double DistancePointToSegment(XYZ point, XYZ a, XYZ b)
		{
			XYZ ab = b - a;
			double len2 = ab.DotProduct(ab);
			if (len2 < 1e-12) return point.DistanceTo(a);
			double t = (point - a).DotProduct(ab) / len2;
			t = Math.Max(0.0, Math.Min(1.0, t));
			return point.DistanceTo(a + t * ab);
		}

		private bool TryGetPatOnlyCurves(UIDocument uidoc, Document doc, Reference reference, Element hostElement, PlanarFace face, out List<Curve> curves)
		{
			curves = new List<Curve>();
			FillPatternElement patternElement = this.GetSelectedFaceSurfacePattern(doc, hostElement, face);
			string patDiagnostic;
			List<Line> lines = this.BuildPatOnlySplitLines(doc, uidoc.ActiveView, reference, face, patternElement, out patDiagnostic);
			if (lines.Count > 0)
			{
				curves.AddRange(lines.Cast<Curve>());
				_patOnlySketchPlane = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
				this.ViewModel.SetStatus("已將畫面上的 Model PAT 轉成 " + lines.Count.ToString() + " 條 Curve，準備執行 Divide Parts。");
				return true;
			}

			TaskDialog.Show("Tile Parts", "無法把畫面 Model PAT 轉成可分割 Curve。\n\n" + patDiagnostic + "\n\n本功能現在只依照 .pat，不使用尺寸或排版備援。請確認材質前景填充線是 Model Pattern，不是 Drafting Pattern 或 Solid Fill。");
			return false;
		}

		private TilePatternParams GetPluginOwnedTilePatternParams()
		{
			TilePatternParams p = new TilePatternParams
			{
				Style = TilePatternStyle.Stack,
				TileWidth = Math.Max(1.0, this.ViewModel.TileWidth),
				TileHeight = Math.Max(1.0, this.ViewModel.TileHeight),
				JointWidth = Math.Max(0.0, this.ViewModel.JointWidth),
				Thickness = Math.Max(0.1, this.ViewModel.TileThickness),
				OffsetPercent = 0.0
			};

			this.ViewModel.TileWidth = p.TileWidth;
			this.ViewModel.TileHeight = p.TileHeight;
			this.ViewModel.JointWidth = p.JointWidth;
			this.ViewModel.TileThickness = p.Thickness;
			return p;
		}

		private string GetPluginOwnedMaterialName(TilePatternParams p)
		{
			if (p == null) return string.Empty;
			double shortSide = Math.Min(p.TileWidth, p.TileHeight);
			double longSide = Math.Max(p.TileWidth, p.TileHeight);
			return string.Format("TileMAT_{0}_{1}x{2}",
				(int)p.Style,
				(int)shortSide,
				(int)longSide);
		}

		private TilePatternParams ResolveMaterialDrivenTilePatternParams(Document doc, Element hostElement, PlanarFace face, TilePatternParams fallback)
		{
			TilePatternParams p = new TilePatternParams
			{
				Style = fallback == null ? TilePatternStyle.Stack : fallback.Style,
				TileWidth = fallback == null ? this.ViewModel.TileWidth : fallback.TileWidth,
				TileHeight = fallback == null ? this.ViewModel.TileHeight : fallback.TileHeight,
				JointWidth = fallback == null ? this.ViewModel.JointWidth : fallback.JointWidth,
				Thickness = fallback == null ? this.ViewModel.TileThickness : fallback.Thickness,
				OffsetPercent = fallback == null ? 0.0 : fallback.OffsetPercent
			};

			Material material = this.GetSelectedFaceMaterial(doc, hostElement, face);
			if (material == null) return p;

			TilePatternParams fromMaterial = this.ParseParamsFromMatName(material.Name);
			if (fromMaterial != null)
			{
				p.Style = fromMaterial.Style;
				p.TileWidth = fromMaterial.TileWidth;
				p.TileHeight = fromMaterial.TileHeight;
				p.JointWidth = fromMaterial.JointWidth;
			}
			else if (TileDimensionDetector.ParseDimensions(material.Name, out double width, out double height))
			{
				p.TileWidth = width;
				p.TileHeight = height;
			}

			try
			{
				FillPatternElement fillPatternElement = material.SurfaceForegroundPatternId == ElementId.InvalidElementId
					? null
					: doc.GetElement(material.SurfaceForegroundPatternId) as FillPatternElement;
				TilePatternParams fromPattern = fillPatternElement == null ? null : this.ParseParamsFromPatName(fillPatternElement.Name);
				if (fromPattern != null)
				{
					p.Style = fromPattern.Style;
					p.TileWidth = fromPattern.TileWidth;
					p.TileHeight = fromPattern.TileHeight;
					p.JointWidth = fromPattern.JointWidth;
				}
			}
			catch { }

			return p;
		}

		private bool TryPickTileLayoutControl(UIDocument uidoc, out XYZ origin, out XYZ direction)
		{
			origin = null;
			direction = null;
			try
			{
				Reference originReference = uidoc.Selection.PickObject(ObjectType.PointOnElement, "Pick tile layout origin point.");
				if (originReference == null || originReference.GlobalPoint == null) return false;

				origin = originReference.GlobalPoint;
				if (_tileLayoutControlUDir != null && _tileLayoutControlUDir.GetLength() > 0.001)
				{
					direction = _tileLayoutControlUDir.Normalize();
					return true;
				}

				direction = XYZ.BasisX;
				return true;
			}
			catch (Autodesk.Revit.Exceptions.OperationCanceledException)
			{
				return false;
			}
			catch { }

			return false;
		}

		private string GetTileLayoutSpaceKey(ElementId roomId, string spaceLabel)
		{
			if (roomId != null && roomId != ElementId.InvalidElementId)
			{
				return "Room:" + roomId.IntegerValue.ToString(CultureInfo.InvariantCulture);
			}

			string label = string.IsNullOrWhiteSpace(spaceLabel) ? "Unassigned" : spaceLabel.Trim();
			return "Label:" + this.CleanGroupName(label);
		}

		private bool TryResolveActiveTileLayoutSpace(UIDocument uidoc, Document doc, bool promptIfMissing, out Room room, out string spaceLabel, out ElementId roomId, out string spaceKey)
		{
			room = null;
			spaceLabel = string.IsNullOrWhiteSpace(_currentTileLayoutSpaceLabel) ? "未指定空間" : _currentTileLayoutSpaceLabel;
			roomId = _currentTileLayoutRoomId;
			spaceKey = this.GetTileLayoutSpaceKey(roomId, spaceLabel);

			HashSet<ElementId> selectedRoomIds = this.GetSelectedRoomIds(doc, uidoc == null ? null : uidoc.Selection.GetElementIds());
			if (selectedRoomIds.Count == 1)
			{
				roomId = selectedRoomIds.First();
				room = doc.GetElement(roomId) as Room;
			}
			else if (roomId != ElementId.InvalidElementId)
			{
				room = doc.GetElement(roomId) as Room;
			}
			else
			{
				List<ElementId> rememberedRooms = _savedTileLayoutFaceReferences
					.Where(saved => saved != null && saved.RoomId != ElementId.InvalidElementId)
					.Select(saved => saved.RoomId)
					.GroupBy(id => id.IntegerValue)
					.Select(group => group.First())
					.ToList();
				if (rememberedRooms.Count == 1)
				{
					roomId = rememberedRooms[0];
					room = doc.GetElement(roomId) as Room;
				}
			}

			if (room == null && promptIfMissing && uidoc != null)
			{
				try
				{
					Reference roomReference = uidoc.Selection.PickObject(ObjectType.Element, new RoomSelectionFilter(), "Select the room for this tile layout control.");
					room = roomReference == null ? null : doc.GetElement(roomReference.ElementId) as Room;
					if (room != null) roomId = room.Id;
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					return false;
				}
			}

			if (room != null)
			{
				spaceLabel = this.GetRoomScheduleLabel(room);
				roomId = room.Id;
			}

			if (string.IsNullOrWhiteSpace(spaceLabel)) spaceLabel = "未指定空間";
			spaceKey = this.GetTileLayoutSpaceKey(roomId, spaceLabel);
			_currentTileLayoutSpaceLabel = spaceLabel;
			_currentTileLayoutRoomId = roomId;
			return room != null || roomId != ElementId.InvalidElementId || !promptIfMissing;
		}

		private bool TryPickTileLayoutSpaceForControl(UIDocument uidoc, Document doc, out Room room, out string spaceLabel, out ElementId roomId, out string spaceKey)
		{
			room = null;
			spaceLabel = "未指定空間";
			roomId = ElementId.InvalidElementId;
			spaceKey = string.Empty;
			if (uidoc == null || doc == null) return false;

			HashSet<ElementId> selectedRoomIds = this.GetSelectedRoomIds(doc, uidoc.Selection.GetElementIds());
			if (selectedRoomIds.Count == 1)
			{
				roomId = selectedRoomIds.First();
				room = doc.GetElement(roomId) as Room;
			}
			else
			{
				try
				{
					Reference roomReference = uidoc.Selection.PickObject(ObjectType.Element, new RoomSelectionFilter(), "請選取要設定或更新磁磚放樣基準的空間。");
					room = roomReference == null ? null : doc.GetElement(roomReference.ElementId) as Room;
					if (room != null) roomId = room.Id;
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					return false;
				}
			}

			if (room == null)
			{
				TaskDialog.Show("Tile Parts", "請先選取一個空間，控制點會依此空間建立獨立紀錄。");
				return false;
			}

			spaceLabel = this.GetRoomScheduleLabel(room);
			if (string.IsNullOrWhiteSpace(spaceLabel)) spaceLabel = "未指定空間";
			roomId = room.Id;
			spaceKey = this.GetTileLayoutSpaceKey(roomId, spaceLabel);
			_currentTileLayoutSpaceLabel = spaceLabel;
			_currentTileLayoutRoomId = roomId;
			return true;
		}

		private int CountSavedTileLayoutFacesForSpace(string spaceKey, ElementId roomId)
		{
			if (string.IsNullOrWhiteSpace(spaceKey) && (roomId == null || roomId == ElementId.InvalidElementId)) return 0;
			return _savedTileLayoutFaceReferences.Count(saved =>
				saved != null &&
				(string.Equals(saved.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase) ||
				(roomId != null && roomId != ElementId.InvalidElementId && saved.RoomId == roomId)));
		}

		private TileLayoutControlState GetOrCreateTileLayoutControlState(string spaceKey, string spaceLabel, ElementId roomId)
		{
			if (string.IsNullOrWhiteSpace(spaceKey)) spaceKey = this.GetTileLayoutSpaceKey(roomId, spaceLabel);
			TileLayoutControlState state;
			if (!_tileLayoutControlStates.TryGetValue(spaceKey, out state) || state == null)
			{
				state = new TileLayoutControlState();
				state.SpaceKey = spaceKey;
				_tileLayoutControlStates[spaceKey] = state;
			}
			state.SpaceLabel = string.IsNullOrWhiteSpace(spaceLabel) ? "未指定空間" : spaceLabel;
			state.RoomId = roomId;
			return state;
		}

		private void ApplyTileLayoutControlState(TileLayoutControlState state)
		{
			if (state == null) return;
			_tileLayoutControlOrigin = state.Origin;
			_tileLayoutControlUDir = state.UDir;
			_tileLayoutControlIsSet = state.IsSet && state.Origin != null && state.UDir != null;
			_tileLayoutControlMarkerIds = state.MarkerIds.ToList();
			_tileLayoutControlDirectionMarkerId = state.DirectionMarkerId;
			_currentTileLayoutSpaceLabel = string.IsNullOrWhiteSpace(state.SpaceLabel) ? "未指定空間" : state.SpaceLabel;
			_currentTileLayoutRoomId = state.RoomId;
		}

		private void StampTileLayoutElement(Document doc, Element element, string role, string spaceKey, string spaceLabel, ElementId roomId)
		{
			if (doc == null || element == null) return;
			Parameter commentParam = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			if (commentParam == null || commentParam.IsReadOnly) return;
			string comments = commentParam.AsString();
			comments = this.UpsertCommentValue(comments, "TileLayoutRole", role ?? string.Empty);
			comments = this.UpsertCommentValue(comments, "TileLayoutSpaceKey", spaceKey ?? string.Empty);
			comments = this.UpsertCommentValue(comments, "Room_ID", spaceLabel ?? string.Empty);
			comments = this.UpsertCommentValue(comments, "RoomElementId", roomId == null || roomId == ElementId.InvalidElementId ? string.Empty : roomId.IntegerValue.ToString(CultureInfo.InvariantCulture));
			commentParam.Set(comments);
		}

		private bool ElementBelongsToTileLayoutSpace(Element element, string spaceKey)
		{
			if (element == null || string.IsNullOrWhiteSpace(spaceKey)) return false;
			Parameter commentParam = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			return string.Equals(this.ParseCommentValue(comments, "TileLayoutSpaceKey"), spaceKey, StringComparison.OrdinalIgnoreCase);
		}

		private void CreateOrUpdateTileLayoutControlMarker(Document doc, View view, XYZ origin, XYZ direction, string spaceKey, string spaceLabel, ElementId roomId)
		{
			if (doc == null || origin == null || direction == null) return;
			TileLayoutControlState state = this.GetOrCreateTileLayoutControlState(spaceKey, spaceLabel, roomId);
			foreach (ElementId id in state.MarkerIds.ToList())
			{
				try { doc.Delete(id); } catch { }
			}
			foreach (ModelCurve curve in new FilteredElementCollector(doc)
				.OfClass(typeof(CurveElement))
				.Cast<CurveElement>()
				.OfType<ModelCurve>()
				.ToList())
			{
				if (!this.ElementBelongsToTileLayoutSpace(curve, spaceKey)) continue;
				Parameter commentParam = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string role = this.ParseCommentValue(commentParam == null ? null : commentParam.AsString(), "TileLayoutRole");
				if (string.Equals(role, "SetoutMarker", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(role, "SetoutDirection", StringComparison.OrdinalIgnoreCase))
				{
					try { doc.Delete(curve.Id); } catch { }
				}
			}
			state.MarkerIds.Clear();
			state.DirectionMarkerId = ElementId.InvalidElementId;

			XYZ uDir = new XYZ(direction.X, direction.Y, 0.0);
			if (uDir.GetLength() < 0.001) uDir = XYZ.BasisX;
			uDir = uDir.Normalize();
			XYZ vDir = XYZ.BasisZ.CrossProduct(uDir);
			if (vDir.GetLength() < 0.001) vDir = XYZ.BasisY;
			vDir = vDir.Normalize();

			double tileSizeFt = Math.Max(this.ViewModel.TileWidth, this.ViewModel.TileHeight) / 304.8;
			double arrowLength = Math.Max(0.75, Math.Min(3.0, tileSizeFt));
			double crossHalf = Math.Max(0.25, Math.Min(0.75, arrowLength * 0.25));
			double arrowHead = Math.Max(0.15, Math.Min(0.35, arrowLength * 0.18));

			Plane planeDefinition = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, origin);
			SketchPlane sketchPlane = SketchPlane.Create(doc, planeDefinition);
			List<Line> markerLines = new List<Line>
			{
				Line.CreateBound(origin - uDir * crossHalf, origin + uDir * crossHalf),
				Line.CreateBound(origin - vDir * crossHalf, origin + vDir * crossHalf),
				Line.CreateBound(origin, origin + uDir * arrowLength),
				Line.CreateBound(origin + uDir * arrowLength, origin + uDir * (arrowLength - arrowHead) + vDir * arrowHead),
				Line.CreateBound(origin + uDir * arrowLength, origin + uDir * (arrowLength - arrowHead) - vDir * arrowHead)
			};

			for (int i = 0; i < markerLines.Count; i++)
			{
				try
				{
					ModelCurve modelCurve = doc.Create.NewModelCurve(markerLines[i], sketchPlane);
					if (modelCurve == null) continue;
					this.ApplyTileSetoutLineStyle(doc, modelCurve);
					this.StampTileLayoutElement(doc, modelCurve, i == 2 ? "SetoutDirection" : "SetoutMarker", spaceKey, spaceLabel, roomId);
					state.MarkerIds.Add(modelCurve.Id);
					if (i == 2) state.DirectionMarkerId = modelCurve.Id;
				}
				catch { }
			}
			state.Origin = origin;
			state.UDir = uDir;
			state.IsSet = true;
			this.ApplyTileLayoutControlState(state);
			this.EnsureTilePreviewLineStylesVisible(doc, view);
		}

		private bool RefreshTileLayoutControlFromMarker(Document doc)
		{
			string spaceKey = this.GetTileLayoutSpaceKey(_currentTileLayoutRoomId, _currentTileLayoutSpaceLabel);
			return this.RefreshTileLayoutControlFromMarker(doc, spaceKey);
		}

		private bool RefreshTileLayoutControlFromMarker(Document doc, string spaceKey)
		{
			if (doc == null || string.IsNullOrWhiteSpace(spaceKey)) return _tileLayoutControlIsSet;
			TileLayoutControlState state;
			_tileLayoutControlStates.TryGetValue(spaceKey, out state);
			ElementId directionMarkerId = state == null ? ElementId.InvalidElementId : state.DirectionMarkerId;
			if (directionMarkerId == ElementId.InvalidElementId)
			{
				ModelCurve found = new FilteredElementCollector(doc)
					.OfClass(typeof(CurveElement))
					.Cast<CurveElement>()
					.OfType<ModelCurve>()
					.FirstOrDefault(curve =>
					{
						Parameter commentParam = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
						string comments = commentParam == null ? null : commentParam.AsString();
						return string.Equals(this.ParseCommentValue(comments, "TileLayoutSpaceKey"), spaceKey, StringComparison.OrdinalIgnoreCase) &&
							string.Equals(this.ParseCommentValue(comments, "TileLayoutRole"), "SetoutDirection", StringComparison.OrdinalIgnoreCase);
					});
				if (found != null) directionMarkerId = found.Id;
			}
			if (directionMarkerId == ElementId.InvalidElementId) return state != null && state.IsSet;
			ModelCurve directionCurve = doc.GetElement(directionMarkerId) as ModelCurve;
			Line directionLine = directionCurve == null ? null : directionCurve.GeometryCurve as Line;
			if (directionLine == null) return state != null && state.IsSet;

			XYZ origin = directionLine.GetEndPoint(0);
			XYZ rawDirection = directionLine.GetEndPoint(1) - origin;
			XYZ horizontalDirection = new XYZ(rawDirection.X, rawDirection.Y, 0.0);
			if (horizontalDirection.GetLength() < 0.001) return state != null && state.IsSet;

			if (state == null)
			{
				state = this.GetOrCreateTileLayoutControlState(spaceKey, _currentTileLayoutSpaceLabel, _currentTileLayoutRoomId);
			}
			state.Origin = origin;
			state.UDir = horizontalDirection.Normalize();
			state.IsSet = true;
			state.DirectionMarkerId = directionMarkerId;
			if (!state.MarkerIds.Contains(directionMarkerId)) state.MarkerIds.Add(directionMarkerId);
			this.ApplyTileLayoutControlState(state);
			return true;
		}

		private void GetTileLayoutFrameForFace(PlanarFace face, out XYZ origin, out XYZ uDir, out XYZ vDir)
		{
			XYZ normal = face.FaceNormal.Normalize();
			origin = _tileLayoutControlIsSet && _tileLayoutControlOrigin != null
				? this.ProjectPointToPlanarFace(_tileLayoutControlOrigin, face)
				: face.Origin;

			XYZ preferred = _tileLayoutControlIsSet && _tileLayoutControlUDir != null
				? _tileLayoutControlUDir
				: face.XVector;

			uDir = preferred - preferred.DotProduct(normal) * normal;
			if (uDir.GetLength() < 0.001 && Math.Abs(normal.Z) < 0.5)
			{
				uDir = XYZ.BasisZ.CrossProduct(normal);
			}
			if (uDir.GetLength() < 0.001)
			{
				uDir = XYZ.BasisX - XYZ.BasisX.DotProduct(normal) * normal;
			}
			if (uDir.GetLength() < 0.001)
			{
				uDir = XYZ.BasisY - XYZ.BasisY.DotProduct(normal) * normal;
			}
			uDir = uDir.Normalize();

			vDir = normal.CrossProduct(uDir);
			if (vDir.GetLength() < 0.001)
			{
				vDir = face.YVector.Normalize();
			}
			else
			{
				vDir = vDir.Normalize();
			}

			if (Math.Abs(normal.Z) < 0.5 && vDir.DotProduct(XYZ.BasisZ) < 0.0)
			{
				uDir = uDir.Negate();
				vDir = vDir.Negate();
			}
		}

		private List<Line> BuildPluginOwnedPatternLines(PlanarFace face, TilePatternParams p)
		{
			XYZ origin;
			XYZ uDir;
			XYZ vDir;
			this.GetTileLayoutFrameForFace(face, out origin, out uDir, out vDir);
			return this.BuildPluginOwnedPatternLines(face, p, origin, uDir, vDir);
		}

		private List<Line> BuildPluginOwnedPatternLines(PlanarFace face, TilePatternParams p, XYZ origin, XYZ uDir, XYZ vDir)
		{
			List<Line> lines = new List<Line>();
			if (face == null || p == null) return lines;

			List<XYZ> points = this.GetProjectedFacePoints(face);
			if (points.Count == 0) return lines;

			XYZ normal = face.FaceNormal.Normalize();
			List<FillGrid> grids = this.BuildFillGrids(p);

			foreach (FillGrid grid in grids)
			{
				if (grid == null || Math.Abs(grid.Offset) < 0.0001) continue;

				double angle = grid.Angle;
				XYZ direction = (Math.Cos(angle) * uDir + Math.Sin(angle) * vDir);
				if (direction.GetLength() < 0.0001) continue;
				direction = direction.Normalize();

				XYZ perpendicular = normal.CrossProduct(direction);
				if (perpendicular.GetLength() < 0.0001) continue;
				perpendicular = perpendicular.Normalize();

				UV gridOrigin = grid.Origin;
				XYZ basePoint = origin + gridOrigin.U * uDir + gridOrigin.V * vDir;
				double minDistance = points.Min(pt => (pt - basePoint).DotProduct(perpendicular));
				double maxDistance = points.Max(pt => (pt - basePoint).DotProduct(perpendicular));
				double spacing = Math.Abs(grid.Offset);
				int start = (int)Math.Floor(minDistance / spacing) - 2;
				int end = (int)Math.Ceiling(maxDistance / spacing) + 2;

				for (int ordinal = start; ordinal <= end; ordinal++)
				{
					XYZ pointOnLine = basePoint + perpendicular * (ordinal * spacing);
					foreach (Line line in this.CreateFaceSpanPatternLines(face, pointOnLine, direction, grid, ordinal))
					{
						this.TryAddUniquePatLine(lines, line, normal);
					}
				}
			}

			return lines;
		}

		private List<Line> BuildFaceBoundaryPreviewLines(PlanarFace face)
		{
			List<Line> lines = new List<Line>();
			if (face == null) return lines;
			XYZ normal = face.FaceNormal.Normalize();

			foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
			{
				List<XYZ> loopPoints = new List<XYZ>();
				foreach (Curve curve in loop)
				{
					IList<XYZ> points = curve.Tessellate();
					if (points == null) continue;

					foreach (XYZ point in points)
					{
						XYZ projected = this.ProjectPointToPlanarFace(point, face);
						if (loopPoints.Count == 0 || loopPoints.Last().DistanceTo(projected) > 0.001)
						{
							loopPoints.Add(projected);
						}
					}
				}

				for (int i = 0; i < loopPoints.Count - 1; i++)
				{
					this.TryAddBoundaryPreviewLine(lines, loopPoints[i], loopPoints[i + 1], normal);
				}
				if (loopPoints.Count > 2)
				{
					this.TryAddBoundaryPreviewLine(lines, loopPoints.Last(), loopPoints.First(), normal);
				}
			}

			if (lines.Count == 0)
			{
				XYZ origin;
				XYZ uDir;
				XYZ vDir;
				this.GetTileLayoutFrameForFace(face, out origin, out uDir, out vDir);
				List<XYZ> points = this.GetProjectedFacePoints(face);
				if (points.Count > 0)
				{
					double minU = points.Min(p => (p - origin).DotProduct(uDir));
					double maxU = points.Max(p => (p - origin).DotProduct(uDir));
					double minV = points.Min(p => (p - origin).DotProduct(vDir));
					double maxV = points.Max(p => (p - origin).DotProduct(vDir));
					XYZ a = origin + minU * uDir + minV * vDir;
					XYZ b = origin + maxU * uDir + minV * vDir;
					XYZ c = origin + maxU * uDir + maxV * vDir;
					XYZ d = origin + minU * uDir + maxV * vDir;
					this.TryAddBoundaryPreviewLine(lines, a, b, normal);
					this.TryAddBoundaryPreviewLine(lines, b, c, normal);
					this.TryAddBoundaryPreviewLine(lines, c, d, normal);
					this.TryAddBoundaryPreviewLine(lines, d, a, normal);
				}
			}

			return lines;
		}

		private List<Line> BuildFaceBoundaryPreviewLines(Document doc, Element hostElement, PlanarFace face)
		{
			List<Line> lines = this.BuildFaceBoundaryPreviewLines(face);
			foreach (Line openingLine in this.BuildHostOpeningBoundaryPreviewLines(doc, hostElement, face))
			{
				this.TryAddBoundaryPreviewLine(lines, openingLine.GetEndPoint(0), openingLine.GetEndPoint(1), face.FaceNormal.Normalize());
			}
			return lines;
		}

		private List<Line> BuildHostOpeningBoundaryPreviewLines(Document doc, Element hostElement, PlanarFace face)
		{
			List<Line> lines = new List<Line>();
			if (doc == null || hostElement == null || face == null) return lines;

			List<ElementId> openingIds = this.GetHostedOpeningElementIds(doc, hostElement);
			if (openingIds.Count == 0) return lines;

			XYZ origin;
			XYZ uDir;
			XYZ vDir;
			this.GetTileLayoutFrameForFace(face, out origin, out uDir, out vDir);
			List<XYZ> facePoints = this.GetProjectedFacePoints(face);
			if (facePoints.Count == 0) return lines;

			double faceMinU = facePoints.Min(p => (p - origin).DotProduct(uDir));
			double faceMaxU = facePoints.Max(p => (p - origin).DotProduct(uDir));
			double faceMinV = facePoints.Min(p => (p - origin).DotProduct(vDir));
			double faceMaxV = facePoints.Max(p => (p - origin).DotProduct(vDir));
			XYZ normal = face.FaceNormal.Normalize();

			foreach (ElementId id in openingIds.Distinct())
			{
				Element opening = doc.GetElement(id);
				if (opening == null) continue;

				BoundingBoxXYZ box = null;
				try { box = opening.get_BoundingBox(null); } catch { }
				if (box == null) continue;

				List<XYZ> corners = this.GetBoundingBoxCorners(box)
					.Select(p => this.ProjectPointToPlanarFace(p, face))
					.ToList();
				if (corners.Count == 0) continue;

				double minU = corners.Min(p => (p - origin).DotProduct(uDir));
				double maxU = corners.Max(p => (p - origin).DotProduct(uDir));
				double minV = corners.Min(p => (p - origin).DotProduct(vDir));
				double maxV = corners.Max(p => (p - origin).DotProduct(vDir));

				minU = Math.Max(minU, faceMinU);
				maxU = Math.Min(maxU, faceMaxU);
				minV = Math.Max(minV, faceMinV);
				maxV = Math.Min(maxV, faceMaxV);
				if (maxU - minU < 0.05 || maxV - minV < 0.05) continue;

				XYZ a = origin + minU * uDir + minV * vDir;
				XYZ b = origin + maxU * uDir + minV * vDir;
				XYZ c = origin + maxU * uDir + maxV * vDir;
				XYZ d = origin + minU * uDir + maxV * vDir;
				this.TryAddBoundaryPreviewLine(lines, a, b, normal);
				this.TryAddBoundaryPreviewLine(lines, b, c, normal);
				this.TryAddBoundaryPreviewLine(lines, c, d, normal);
				this.TryAddBoundaryPreviewLine(lines, d, a, normal);
			}

			return lines;
		}

		private List<ElementId> GetHostedOpeningElementIds(Document doc, Element hostElement)
		{
			List<ElementId> ids = new List<ElementId>();
			if (doc == null || hostElement == null) return ids;

			Wall wall = hostElement as Wall;
			if (wall != null)
			{
				try
				{
					ids.AddRange(wall.FindInserts(true, true, true, true));
				}
				catch { }
			}

			BuiltInCategory[] categories =
			{
				BuiltInCategory.OST_Doors,
				BuiltInCategory.OST_Windows
			};

			foreach (BuiltInCategory category in categories)
			{
				try
				{
					foreach (FamilyInstance familyInstance in new FilteredElementCollector(doc)
						.OfCategory(category)
						.WhereElementIsNotElementType()
						.OfType<FamilyInstance>())
					{
						if (familyInstance.Host != null && familyInstance.Host.Id == hostElement.Id)
						{
							ids.Add(familyInstance.Id);
						}
					}
				}
				catch { }
			}

			return ids.Distinct().ToList();
		}

		private List<XYZ> GetBoundingBoxCorners(BoundingBoxXYZ box)
		{
			List<XYZ> corners = new List<XYZ>();
			if (box == null) return corners;
			Transform transform = box.Transform ?? Transform.Identity;
			foreach (double x in new[] { box.Min.X, box.Max.X })
			{
				foreach (double y in new[] { box.Min.Y, box.Max.Y })
				{
					foreach (double z in new[] { box.Min.Z, box.Max.Z })
					{
						corners.Add(transform.OfPoint(new XYZ(x, y, z)));
					}
				}
			}
			return corners;
		}

		private void TryAddBoundaryPreviewLine(List<Line> lines, XYZ start, XYZ end, XYZ normal)
		{
			if (lines == null || start == null || end == null) return;
			if (start.DistanceTo(end) < 0.005) return;
			try
			{
				Line line = Line.CreateBound(start, end);
				this.TryAddUniquePatLine(lines, line, normal);
			}
			catch { }
		}

		private XYZ ProjectPointToPlane(XYZ point, Plane plane)
		{
			if (point == null || plane == null) return point;
			XYZ normal = plane.Normal.Normalize();
			return point - normal * (point - plane.Origin).DotProduct(normal);
		}

		private ModelCurve CreatePreviewModelCurve(Document doc, SketchPlane sketchPlane, Plane plane, Curve curve)
		{
			if (doc == null || sketchPlane == null || plane == null || curve == null) return null;
			try
			{
				return doc.Create.NewModelCurve(curve, sketchPlane);
			}
			catch
			{
				Line line = curve as Line;
				if (line == null) return null;
				try
				{
					XYZ start = this.ProjectPointToPlane(line.GetEndPoint(0), plane);
					XYZ end = this.ProjectPointToPlane(line.GetEndPoint(1), plane);
					if (start == null || end == null || start.DistanceTo(end) < 0.005) return null;
					return doc.Create.NewModelCurve(Line.CreateBound(start, end), sketchPlane);
				}
				catch { }
			}
			return null;
		}

		private TileBoundaryJoinMode GetSelectedBoundaryJoinMode()
		{
			int index = this.ViewModel == null ? 0 : this.ViewModel.TileBoundaryJoinModeIndex;
			if (index == 1) return TileBoundaryJoinMode.IndependentToBoundary;
			if (index == 2) return TileBoundaryJoinMode.OuterCornerWrap;
			if (index == 3) return TileBoundaryJoinMode.InnerCornerGroutBreak;
			return TileBoundaryJoinMode.MiterJoin;
		}

		private string GetBoundaryJoinModeLabel(TileBoundaryJoinMode mode)
		{
			switch (mode)
			{
				case TileBoundaryJoinMode.MiterJoin:
					return "斜角接合";
				case TileBoundaryJoinMode.OuterCornerWrap:
					return "外角連續包角";
				case TileBoundaryJoinMode.InnerCornerGroutBreak:
					return "內角留縫斷開";
				default:
					return "各面獨立到邊界";
			}
		}

		private void EnsureTilePreviewLineStylesVisible(Document doc, View view)
		{
			if (doc == null || view == null) return;
			try
			{
				Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
				foreach (string name in new[] { "TilePAT_Captured", "TilePAT_Boundary", "TilePAT_Setout" })
				{
					Category category = linesCategory.SubCategories
						.Cast<Category>()
						.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
					if (category == null) continue;
					try
					{
						if (view.CanCategoryBeHidden(category.Id))
						{
							view.SetCategoryHidden(category.Id, false);
						}
					}
					catch { }
				}
			}
			catch { }
		}

		private bool TryPaintPluginOwnedPattern(Document doc, Reference reference, Element hostElement, PlanarFace face, ElementId materialId, out string diagnostic)
		{
			diagnostic = string.Empty;
			if (doc == null || reference == null || hostElement == null || face == null || materialId == ElementId.InvalidElementId)
			{
				diagnostic = "缺少選取面或材質，無法套用外掛 PAT。";
				return false;
			}

			List<ElementId> targetIds = new List<ElementId>();
			if (reference.ElementId != ElementId.InvalidElementId) targetIds.Add(reference.ElementId);
			if (hostElement.Id != ElementId.InvalidElementId && !targetIds.Contains(hostElement.Id)) targetIds.Add(hostElement.Id);

			List<string> errors = new List<string>();
			bool anyPainted = false;
			foreach (ElementId id in targetIds)
			{
				try
				{
					if (doc.IsPainted(id, face)) doc.RemovePaint(id, face);
					doc.Paint(id, face, materialId);
					anyPainted = true;
				}
				catch (Exception ex)
				{
					errors.Add(ex.Message);
				}
			}

			if (anyPainted) return true;
			diagnostic = errors.Count == 0 ? "Revit 拒絕在此面套用材質 PAT。" : string.Join("\n", errors.Distinct());
			return false;
		}

		private void DoStep1CreateRawParts(UIDocument uidoc, Document doc)
		{
			Room room;
			string spaceLabel;
			ElementId roomId;
			string spaceKey;
			if (!this.TryPickTileLayoutSpaceForControl(uidoc, doc, out room, out spaceLabel, out roomId, out spaceKey)) return;

			XYZ origin;
			XYZ direction;
			if (!this.TryPickTileLayoutControl(uidoc, out origin, out direction)) return;

			_tileLayoutControlOrigin = origin;
			_tileLayoutControlUDir = direction;
			_tileLayoutControlIsSet = true;
			using (Transaction t = new Transaction(doc, "Create tile setout control marker"))
			{
				t.Start();
				this.CreateOrUpdateTileLayoutControlMarker(doc, uidoc.ActiveView, origin, direction, spaceKey, spaceLabel, roomId);
				t.Commit();
			}
			if (_tileLayoutControlMarkerIds.Count > 0)
			{
				uidoc.Selection.SetElementIds(_tileLayoutControlMarkerIds);
				uidoc.RefreshActiveView();
			}
			this.ViewModel?.SetTileLayoutRecordStatus(
				spaceLabel,
				spaceKey,
				true,
				this.CountSavedTileLayoutFacesForSpace(spaceKey, roomId),
				0,
				"已切換到此空間紀錄；控制點更新後請執行 Step 2 重建此空間分割線");
			this.ViewModel.SetStatus(string.Format(
				"已建立可拖動放樣控制標記：原點 ({0:F3}, {1:F3}, {2:F3})，主方向 ({3:F3}, {4:F3}, {5:F3})。可拖動/旋轉青色標記後再建立控制線。",
				origin.X,
				origin.Y,
				origin.Z,
				direction.X,
				direction.Y,
				direction.Z));
		}

		private bool TryResolvePatFace(Document doc, Reference reference, out Element hostElement, out PlanarFace face)
		{
			hostElement = null;
			face = null;
			if (doc == null || reference == null) return false;

			Element pickedElement = doc.GetElement(reference.ElementId);
			face = pickedElement == null ? null : pickedElement.GetGeometryObjectFromReference(reference) as PlanarFace;
			hostElement = this.ResolvePatOnlyPartsHost(doc, pickedElement);
			return hostElement != null && face != null;
		}

		private void DeletePreviewControlLines(Document doc)
		{
			foreach (ElementId id in _patOnlyPreviewLineIds) { try { doc.Delete(id); } catch { } }
			foreach (ElementId id in _patOnlyBoundaryPreviewLineIds) { try { doc.Delete(id); } catch { } }
			foreach (TileLayoutPreviewSet set in _tileLayoutPreviewSets)
			{
				foreach (ElementId id in set.GridLineIds) { try { doc.Delete(id); } catch { } }
				foreach (ElementId id in set.BoundaryLineIds) { try { doc.Delete(id); } catch { } }
			}
			_patOnlyPreviewLineIds.Clear();
			_patOnlyBoundaryPreviewLineIds.Clear();
			_tileLayoutPreviewSets.Clear();
		}

		private void DeletePreviewControlLines(Document doc, string spaceKey)
		{
			if (doc == null || string.IsNullOrWhiteSpace(spaceKey))
			{
				this.DeletePreviewControlLines(doc);
				return;
			}

			HashSet<int> deletedIds = new HashSet<int>();
			Action<ElementId> deleteIfExists = id =>
			{
				if (id == null || id == ElementId.InvalidElementId || deletedIds.Contains(id.IntegerValue)) return;
				Element element = doc.GetElement(id);
				if (element == null) return;
				try
				{
					doc.Delete(id);
					deletedIds.Add(id.IntegerValue);
				}
				catch { }
			};

			foreach (TileLayoutPreviewSet set in _tileLayoutPreviewSets.Where(set => set != null && string.Equals(set.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase)).ToList())
			{
				foreach (ElementId id in set.GridLineIds) deleteIfExists(id);
				foreach (ElementId id in set.BoundaryLineIds) deleteIfExists(id);
			}

			foreach (ModelCurve curve in new FilteredElementCollector(doc)
				.OfClass(typeof(CurveElement))
				.Cast<CurveElement>()
				.OfType<ModelCurve>())
			{
				if (!this.ElementBelongsToTileLayoutSpace(curve, spaceKey)) continue;
				Parameter commentParam = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				string role = this.ParseCommentValue(commentParam == null ? null : commentParam.AsString(), "TileLayoutRole");
				if (string.Equals(role, "GridLine", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(role, "BoundaryLine", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(role, "OpeningBoundaryLine", StringComparison.OrdinalIgnoreCase))
				{
					deleteIfExists(curve.Id);
				}
			}

			_tileLayoutPreviewSets.RemoveAll(set => set != null && string.Equals(set.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase));
			_patOnlyPreviewLineIds.RemoveAll(id => id != null && deletedIds.Contains(id.IntegerValue));
			_patOnlyBoundaryPreviewLineIds.RemoveAll(id => id != null && deletedIds.Contains(id.IntegerValue));
		}

		private void DoSaveTileLayoutFaces(UIDocument uidoc, Document doc)
		{
			Reference roomReference;
			try
			{
				roomReference = uidoc.Selection.PickObject(ObjectType.Element, new RoomSelectionFilter(), "請先選取這批天地牆磁磚所屬的房間。");
			}
			catch (Autodesk.Revit.Exceptions.OperationCanceledException)
			{
				return;
			}
			Room room = roomReference == null ? null : doc.GetElement(roomReference.ElementId) as Room;
			if (room == null)
			{
				TaskDialog.Show("Tile Parts", "沒有選到有效房間，無法建立空間群組。");
				return;
			}
			string spaceLabel = this.GetRoomScheduleLabel(room);
			_currentTileLayoutSpaceLabel = spaceLabel;
			_currentTileLayoutRoomId = room.Id;

			IList<Reference> references;
			try
			{
				references = uidoc.Selection.PickObjects(ObjectType.Face, "請選取此空間的地坪、牆面、天花板磁磚面。");
			}
			catch (Autodesk.Revit.Exceptions.OperationCanceledException)
			{
				return;
			}
			if (references == null || references.Count == 0) return;

			int savedCount = this.SaveTileLayoutFaceReferences(doc, references, spaceLabel, room.Id);
			if (savedCount == 0)
			{
				TaskDialog.Show("Tile Parts", "沒有成功儲存任何選取面。請確認選取的是地坪、牆面或天花板的平面。");
				return;
			}

			List<ElementId> selectedElementIds = references
				.Where(r => r != null && r.ElementId != ElementId.InvalidElementId)
				.Select(r => r.ElementId)
				.Distinct()
				.ToList();
			if (selectedElementIds.Count > 0) uidoc.Selection.SetElementIds(selectedElementIds);
			this.ViewModel?.SetTileLayoutRecordStatus(
				spaceLabel,
				this.GetTileLayoutSpaceKey(room.Id, spaceLabel),
				null,
				savedCount,
				null,
				"空間面已儲存在此空間紀錄，其他空間不會被覆蓋");
			this.ViewModel.SetStatus(string.Format("已建立空間群組 [{0}]，儲存 {1} 個天地牆磁磚面。Step 2/Step 3 會沿用此群組統計。", spaceLabel, savedCount));
		}

		private int SaveTileLayoutFaceReferences(Document doc, IEnumerable<Reference> references)
		{
			return this.SaveTileLayoutFaceReferences(doc, references, _currentTileLayoutSpaceLabel, _currentTileLayoutRoomId);
		}

		private int SaveTileLayoutFaceReferences(Document doc, IEnumerable<Reference> references, string spaceLabel, ElementId roomId)
		{
			if (doc == null || references == null) return 0;
			if (string.IsNullOrWhiteSpace(spaceLabel)) spaceLabel = "未指定空間";
			string spaceKey = this.GetTileLayoutSpaceKey(roomId, spaceLabel);

			_savedTileLayoutFaceReferences.RemoveAll(saved =>
				saved == null ||
				string.Equals(saved.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase) ||
				(roomId != ElementId.InvalidElementId && saved.RoomId == roomId) ||
				(roomId == ElementId.InvalidElementId && string.Equals(saved.SpaceLabel, spaceLabel, StringComparison.OrdinalIgnoreCase)));

			int added = 0;
			HashSet<string> seenStableReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (Reference reference in references)
			{
				if (reference == null) continue;
				try
				{
					string stable = reference.ConvertToStableRepresentation(doc);
					if (string.IsNullOrWhiteSpace(stable)) continue;
					if (!seenStableReferences.Add(stable)) continue;
					_savedTileLayoutFaceReferences.Add(new TileSavedFaceReference
					{
						StableReference = stable,
						ElementId = reference.ElementId,
						SpaceLabel = spaceLabel,
						RoomId = roomId,
						SpaceKey = spaceKey
					});
					added++;
				}
				catch { }
			}

			return added;
		}

		private HashSet<ElementId> GetSelectedRoomIds(Document doc, ICollection<ElementId> selectedIds)
		{
			HashSet<ElementId> roomIds = new HashSet<ElementId>();
			if (doc == null || selectedIds == null) return roomIds;

			foreach (ElementId id in selectedIds)
			{
				Room room = doc.GetElement(id) as Room;
				if (room != null) roomIds.Add(room.Id);
			}

			return roomIds;
		}

		private IList<Reference> GetTileLayoutFaceReferences(UIDocument uidoc, Document doc, string spaceKey, string spaceLabel, ElementId roomId, out bool usedSavedReferences)
		{
			usedSavedReferences = false;
			List<Reference> references = new List<Reference>();
			if (doc != null && _savedTileLayoutFaceReferences.Count > 0)
			{
				if (string.IsNullOrWhiteSpace(spaceKey)) spaceKey = this.GetTileLayoutSpaceKey(roomId, spaceLabel);
				List<TileSavedFaceReference> savedScope = _savedTileLayoutFaceReferences
					.Where(saved =>
						saved != null &&
						(string.Equals(saved.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase) ||
						(roomId != ElementId.InvalidElementId && saved.RoomId == roomId)))
					.ToList();

				foreach (TileSavedFaceReference saved in savedScope)
				{
					if (saved == null || string.IsNullOrWhiteSpace(saved.StableReference)) continue;
					try
					{
						Reference reference = Reference.ParseFromStableRepresentation(doc, saved.StableReference);
						if (reference != null) references.Add(reference);
					}
					catch { }
				}
				if (references.Count > 0)
				{
					usedSavedReferences = true;
					return references;
				}

				_savedTileLayoutFaceReferences.RemoveAll(saved =>
					saved != null &&
					(string.Equals(saved.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase) ||
					(roomId != ElementId.InvalidElementId && saved.RoomId == roomId)));
				TaskDialog.Show("Tile Parts", "先前儲存的選取面已失效，請重新選取一次。");
			}

			try
			{
				IList<Reference> picked = uidoc.Selection.PickObjects(ObjectType.Face, "Select floor, wall, ceiling tile faces for layout control.");
				if (picked != null && picked.Count > 0)
				{
					_currentTileLayoutSpaceLabel = string.IsNullOrWhiteSpace(spaceLabel) ? "未指定空間" : spaceLabel;
					_currentTileLayoutRoomId = roomId;
					this.SaveTileLayoutFaceReferences(doc, picked, _currentTileLayoutSpaceLabel, _currentTileLayoutRoomId);
					return picked;
				}
			}
			catch (Autodesk.Revit.Exceptions.OperationCanceledException)
			{
				return references;
			}

			return references;
		}

		private TileSavedFaceReference GetSavedTileFaceReference(Document doc, Reference reference)
		{
			if (doc == null || reference == null) return null;
			string stable = null;
			try { stable = reference.ConvertToStableRepresentation(doc); } catch { }
			if (string.IsNullOrWhiteSpace(stable)) return null;
			return _savedTileLayoutFaceReferences.FirstOrDefault(saved =>
				saved != null && string.Equals(saved.StableReference, stable, StringComparison.OrdinalIgnoreCase));
		}

		private void DoStep2CalculateSplitCurves(UIDocument uidoc, Document doc)
		{
			Room activeRoom;
			string activeSpaceLabel;
			ElementId activeRoomId;
			string activeSpaceKey;
			if (!this.TryResolveActiveTileLayoutSpace(uidoc, doc, true, out activeRoom, out activeSpaceLabel, out activeRoomId, out activeSpaceKey)) return;

			this.RefreshTileLayoutControlFromMarker(doc, activeSpaceKey);
			if (!_tileLayoutControlIsSet || _tileLayoutControlOrigin == null || _tileLayoutControlUDir == null)
			{
				TaskDialog.Show("Tile Parts", "請先按步驟 1 設定磁磚放樣原點與主方向。");
				return;
			}

			bool usedSavedReferences;
			IList<Reference> references = this.GetTileLayoutFaceReferences(uidoc, doc, activeSpaceKey, activeSpaceLabel, activeRoomId, out usedSavedReferences);
			if (references == null || references.Count == 0) return;

			TilePatternParams fallbackParams = this.GetPluginOwnedTilePatternParams();
			_patOnlyTileParams = fallbackParams;
			_patOnlyMaterialName = this.GetPluginOwnedMaterialName(fallbackParams);
			int faceCount = 0;
			int gridCount = 0;
			int boundaryCount = 0;
			List<string> paintDiagnostics = new List<string>();
			List<ElementId> selectablePreviewIds = new List<ElementId>();
			HashSet<string> sizeSummaries = new HashSet<string>();
			TileBoundaryJoinMode joinMode = this.GetSelectedBoundaryJoinMode();
			string joinModeLabel = this.GetBoundaryJoinModeLabel(joinMode);

			using (Transaction t = new Transaction(doc, "Create tile layout control preview"))
			{
				t.Start();
				SharedParameterHelper.RegisterAndBindParameters(doc);
				this.DeletePreviewControlLines(doc, activeSpaceKey);
				this.EnablePatOnlyPartsViews(doc, uidoc.ActiveView);

				foreach (Reference reference in references)
				{
					Element hostElement;
					PlanarFace face;
					if (!this.TryResolvePatFace(doc, reference, out hostElement, out face)) continue;
					TileSavedFaceReference savedFace = this.GetSavedTileFaceReference(doc, reference);
					string spaceLabel = savedFace == null || string.IsNullOrWhiteSpace(savedFace.SpaceLabel)
						? activeSpaceLabel
						: savedFace.SpaceLabel;
					ElementId roomId = savedFace == null ? activeRoomId : savedFace.RoomId;
					string spaceKey = savedFace == null || string.IsNullOrWhiteSpace(savedFace.SpaceKey)
						? activeSpaceKey
						: savedFace.SpaceKey;

					TilePatternParams tileParams = this.ResolveMaterialDrivenTilePatternParams(doc, hostElement, face, fallbackParams);
					ElementId materialId = this.GetOrCreateTileHatchMaterial(doc, tileParams);
					if (materialId == ElementId.InvalidElementId) continue;

					List<Line> generatedLines = this.BuildPluginOwnedPatternLines(face, tileParams);
					if (generatedLines.Count == 0) continue;
					List<Line> faceBoundaryLines = this.BuildFaceBoundaryPreviewLines(face);
					List<Line> openingBoundaryLines = this.BuildHostOpeningBoundaryPreviewLines(doc, hostElement, face);
					List<Line> boundaryLines = faceBoundaryLines.Concat(openingBoundaryLines).ToList();

					string paintDiagnostic;
					bool painted = this.TryPaintPluginOwnedPattern(doc, reference, hostElement, face, materialId, out paintDiagnostic);
					if (!painted && !string.IsNullOrWhiteSpace(paintDiagnostic))
					{
						paintDiagnostics.Add(hostElement.Id.IntegerValue.ToString() + ": " + paintDiagnostic);
					}

					ICollection<ElementId> parts = this.EnsurePatOnlyHostParts(doc, hostElement);
					if (parts == null || parts.Count == 0) continue;

					Plane sketchPlaneDefinition = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
					SketchPlane sketchPlane = SketchPlane.Create(doc, sketchPlaneDefinition);
					TileLayoutPreviewSet set = new TileLayoutPreviewSet
					{
						HostId = hostElement.Id,
						SketchPlane = sketchPlaneDefinition,
						TileParams = tileParams,
						MaterialName = this.GetPluginOwnedMaterialName(tileParams),
						JoinMode = joinMode,
						SpaceLabel = string.IsNullOrWhiteSpace(spaceLabel) ? "未指定空間" : spaceLabel,
						RoomId = roomId,
						SpaceKey = spaceKey
					};

					foreach (Curve curve in generatedLines)
					{
						try
						{
							ModelCurve modelCurve = this.CreatePreviewModelCurve(doc, sketchPlane, sketchPlaneDefinition, curve);
							if (modelCurve != null)
							{
								this.ApplyCapturedPatLineStyle(doc, modelCurve);
								this.StampTileLayoutElement(doc, modelCurve, "GridLine", spaceKey, set.SpaceLabel, roomId);
								set.GridLineIds.Add(modelCurve.Id);
								selectablePreviewIds.Add(modelCurve.Id);
							}
						}
						catch { }
					}

					foreach (Curve curve in faceBoundaryLines)
					{
						try
						{
							ModelCurve modelCurve = this.CreatePreviewModelCurve(doc, sketchPlane, sketchPlaneDefinition, curve);
							if (modelCurve != null)
							{
								this.ApplyPatBoundaryLineStyle(doc, modelCurve);
								this.StampTileLayoutElement(doc, modelCurve, "BoundaryLine", spaceKey, set.SpaceLabel, roomId);
								set.BoundaryLineIds.Add(modelCurve.Id);
								set.GridLineIds.Add(modelCurve.Id);
								selectablePreviewIds.Add(modelCurve.Id);
							}
						}
						catch { }
					}

					foreach (Curve curve in openingBoundaryLines)
					{
						try
						{
							ModelCurve modelCurve = this.CreatePreviewModelCurve(doc, sketchPlane, sketchPlaneDefinition, curve);
							if (modelCurve != null)
							{
								this.ApplyPatBoundaryLineStyle(doc, modelCurve);
								this.StampTileLayoutElement(doc, modelCurve, "OpeningBoundaryLine", spaceKey, set.SpaceLabel, roomId);
								set.BoundaryLineIds.Add(modelCurve.Id);
								set.GridLineIds.Add(modelCurve.Id);
								selectablePreviewIds.Add(modelCurve.Id);
							}
						}
						catch { }
					}

					_tileLayoutPreviewSets.Add(set);
					_patOnlyPreviewLineIds.AddRange(set.GridLineIds);
					_patOnlyBoundaryPreviewLineIds.AddRange(set.BoundaryLineIds);
					_patOnlyHostElement = hostElement;
					_patOnlySketchPlane = sketchPlaneDefinition;
					_patOnlyTileParams = tileParams;
					_patOnlyMaterialName = set.MaterialName;
					_patOnlySplitCurves = generatedLines.Concat(boundaryLines).Cast<Curve>().ToList();
					faceCount++;
					gridCount += set.GridLineIds.Count;
					boundaryCount += set.BoundaryLineIds.Count;
					sizeSummaries.Add(string.Format("{0:0.#}x{1:0.#}/J{2:0.#}", tileParams.TileWidth, tileParams.TileHeight, tileParams.JointWidth));
				}
				this.EnsureTilePreviewLineStylesVisible(doc, uidoc.ActiveView);
				t.Commit();
			}

			if (selectablePreviewIds.Count > 0)
			{
				uidoc.Selection.SetElementIds(selectablePreviewIds);
				uidoc.RefreshActiveView();
			}

			if (faceCount == 0)
			{
				TaskDialog.Show("Tile Parts", "沒有任何選取面成功建立磁磚放樣控制線。請確認選到的是可建立 Parts 的地坪、牆面、天花板平面。");
				return;
			}

			this.ViewModel?.SetTileLayoutRecordStatus(
				activeSpaceLabel,
				activeSpaceKey,
				true,
				references.Count,
				gridCount,
				"控制線已綁定目前空間；重建時只會更新此空間");

			if (paintDiagnostics.Count > 0)
			{
				TaskDialog.Show("Tile Parts", "部分面已建立控制線，但 Revit 未允許套用 PAT 材質：\n\n" + string.Join("\n", paintDiagnostics.Take(8)));
			}

			this.ViewModel.SetStatus(string.Format("磁磚放樣控制已套用 {0} 個面，{1}，交接方式：{2}，建立 {3} 條分割線、{4} 條邊界線。材質尺寸：{5}",
				faceCount,
				usedSavedReferences ? "使用已儲存選取面" : "已更新選取面紀錄",
				joinModeLabel,
				gridCount,
				boundaryCount,
				string.Join(", ", sizeSummaries.Take(5))));
		}

		private void DoStep3ExecuteDivideParts(UIDocument uidoc, Document doc)
		{
			if ((_tileLayoutPreviewSets == null || _tileLayoutPreviewSets.Count == 0) &&
				(_patOnlyHostElement == null || _patOnlySketchPlane == null ||
				((_patOnlySplitCurves == null || _patOnlySplitCurves.Count == 0) && _patOnlyPreviewLineIds.Count == 0)))
			{
				TaskDialog.Show("Tile Parts", "請先按步驟 2 建立磁磚放樣控制線，確認紅色分割線後再執行分割。");
				return;
			}

			PartsTileQuantitySummary totalSummary = new PartsTileQuantitySummary();
			int dividedFaceCount = 0;
			int confirmedControlLineCount = 0;
			Room activeRoom;
			string activeSpaceLabel;
			ElementId activeRoomId;
			string activeSpaceKey;
			bool hasActiveSpace = this.TryResolveActiveTileLayoutSpace(uidoc, doc, false, out activeRoom, out activeSpaceLabel, out activeRoomId, out activeSpaceKey);
			using (Transaction t = new Transaction(doc, "Divide Parts by tile layout control lines"))
			{
				t.Start();
				SharedParameterHelper.RegisterAndBindParameters(doc);

				List<TileLayoutPreviewSet> sets = _tileLayoutPreviewSets != null && _tileLayoutPreviewSets.Count > 0
					? _tileLayoutPreviewSets.ToList()
					: new List<TileLayoutPreviewSet>
					{
						new TileLayoutPreviewSet
						{
							HostId = _patOnlyHostElement.Id,
							SketchPlane = _patOnlySketchPlane,
							TileParams = _patOnlyTileParams,
							MaterialName = _patOnlyMaterialName,
							SpaceLabel = _currentTileLayoutSpaceLabel,
							RoomId = _currentTileLayoutRoomId,
							SpaceKey = this.GetTileLayoutSpaceKey(_currentTileLayoutRoomId, _currentTileLayoutSpaceLabel)
						}
					};
				if (hasActiveSpace && !string.IsNullOrWhiteSpace(activeSpaceKey) && sets.Any(set => set != null && !string.IsNullOrWhiteSpace(set.SpaceKey)))
				{
					sets = sets
						.Where(set => set != null && string.Equals(set.SpaceKey, activeSpaceKey, StringComparison.OrdinalIgnoreCase))
						.ToList();
				}
				if (sets.Count == 0)
				{
					t.RollBack();
					TaskDialog.Show("Tile Parts", "目前空間沒有可確認的控制線，請先針對此空間執行 Step 2。");
					return;
				}
				if (sets.Count == 1 && sets[0].GridLineIds.Count == 0)
				{
					sets[0].GridLineIds.AddRange(_patOnlyPreviewLineIds);
				}
				confirmedControlLineCount = sets.Sum(set => set == null || set.GridLineIds == null ? 0 : set.GridLineIds.Count);

				foreach (TileLayoutPreviewSet set in sets)
				{
					Element hostElement = doc.GetElement(set.HostId);
					if (hostElement == null || set.SketchPlane == null) continue;

					List<Curve> confirmedCurves = this.GetCapturedPatCurvesFromModelCurves(doc, set.GridLineIds);
					if (confirmedCurves.Count == 0) continue;

					Element existingMaker = PartUtils.GetAssociatedPartMaker(doc, hostElement.Id);
					if (existingMaker != null)
					{
						doc.Delete(existingMaker.Id);
						doc.Regenerate();
					}

					ICollection<ElementId> parts = this.EnsurePatOnlyHostParts(doc, hostElement);
					if (!PartUtils.ArePartsValidForDivide(doc, parts)) continue;

					SketchPlane sketchPlane = SketchPlane.Create(doc, set.SketchPlane);
					PartMaker maker = PartUtils.DivideParts(doc, parts, new List<ElementId>(), confirmedCurves, sketchPlane.Id);
					doc.Regenerate();
					if (!this.VerifyPartsHaveSplittingCurves(doc, hostElement, maker))
					{
						continue;
					}

					_patOnlyTileParams = set.TileParams ?? _patOnlyTileParams;
					_patOnlyMaterialName = set.MaterialName ?? _patOnlyMaterialName;
					_patOnlySketchPlane = set.SketchPlane;
					_currentTileLayoutSpaceLabel = string.IsNullOrWhiteSpace(set.SpaceLabel) ? "未指定空間" : set.SpaceLabel;
					_currentTileLayoutRoomId = set.RoomId;
					PartsTileQuantitySummary summary = this.AnnotateDividedTileParts(doc, hostElement);
					totalSummary.Total += summary.Total;
					totalSummary.Full += summary.Full;
					totalSummary.HalfCut += summary.HalfCut;
					totalSummary.UnderHalfCut += summary.UnderHalfCut;
					totalSummary.Cut += summary.Cut;
					totalSummary.AreaSqm += summary.AreaSqm;
					foreach (KeyValuePair<string, int> surfaceTotal in summary.SurfaceTotals)
					{
						if (!totalSummary.SurfaceTotals.ContainsKey(surfaceTotal.Key)) totalSummary.SurfaceTotals[surfaceTotal.Key] = 0;
						totalSummary.SurfaceTotals[surfaceTotal.Key] += surfaceTotal.Value;
					}
					dividedFaceCount++;
				}

				this.EnablePatOnlyPartsViews(doc, uidoc.ActiveView);
				t.Commit();
			}

			if (dividedFaceCount == 0)
			{
				TaskDialog.Show("Tile Parts", "沒有任何面成功執行 Divide Parts。請確認紅色控制線仍存在，且選取的是可建立 Parts 的地坪、牆面或天花板面。");
				return;
			}

			if (totalSummary != null)
			{
				this.ViewModel?.SetTileLayoutRecordStatus(
					string.IsNullOrWhiteSpace(activeSpaceLabel) ? _currentTileLayoutSpaceLabel : activeSpaceLabel,
					string.IsNullOrWhiteSpace(activeSpaceKey) ? this.GetTileLayoutSpaceKey(_currentTileLayoutRoomId, _currentTileLayoutSpaceLabel) : activeSpaceKey,
					true,
					null,
					confirmedControlLineCount,
					"已使用此空間的控制點與分割線完成 Divide Parts");
				string surfaceSummary = totalSummary.SurfaceTotals.Count == 0
					? string.Empty
					: "，分類：" + string.Join("、", totalSummary.SurfaceTotals.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value));
				this.ViewModel.SetStatus(string.Format(
					"磁磚放樣分割完成：面 {0}，總數 {1}，整磚 {2}，裁切磚 {3}，半磚 {4}，不足半磚 {5}，面積 {6:F2} ㎡{7}",
					dividedFaceCount,
					totalSummary.Total,
					totalSummary.Full,
					totalSummary.Cut,
					totalSummary.HalfCut,
					totalSummary.UnderHalfCut,
					totalSummary.AreaSqm,
					surfaceSummary));
			}
		}

		private void CapturePatOnlyQuantityInfo(Document doc, Element hostElement, PlanarFace face)
		{
			_patOnlyTileParams = null;
			_patOnlyMaterialName = string.Empty;
			Material material = this.GetSelectedFaceMaterial(doc, hostElement, face);
			if (material != null)
			{
				_patOnlyMaterialName = material.Name;
				TilePatternParams fromMaterial = this.ParseParamsFromMatName(material.Name);
				if (fromMaterial != null)
				{
					_patOnlyTileParams = fromMaterial;
					return;
				}

				if (TileDimensionDetector.ParseDimensions(material.Name, out double width, out double height))
				{
					_patOnlyTileParams = new TilePatternParams
					{
						TileWidth = width,
						TileHeight = height,
						JointWidth = this.ViewModel.JointWidth,
						Thickness = this.ViewModel.TileThickness,
						Style = TilePatternStyle.Stack
					};
					return;
				}

				try
				{
					FillPatternElement fillPatternElement = material.SurfaceForegroundPatternId == ElementId.InvalidElementId
						? null
						: doc.GetElement(material.SurfaceForegroundPatternId) as FillPatternElement;
					TilePatternParams fromPattern = fillPatternElement == null ? null : this.ParseParamsFromPatName(fillPatternElement.Name);
					if (fromPattern != null)
					{
						_patOnlyTileParams = fromPattern;
					}
				}
				catch { }
			}

			if (_patOnlyTileParams == null)
			{
				_patOnlyTileParams = TileDimensionDetector.DetectPatternParams(
					hostElement,
					doc,
					this.ViewModel.TileWidth,
					this.ViewModel.TileHeight,
					this.ViewModel.JointWidth,
					this.ViewModel.TileThickness);
			}
		}

		private PartsTileQuantitySummary AnnotateDividedTileParts(Document doc, Element hostElement)
		{
			PartsTileQuantitySummary summary = new PartsTileQuantitySummary();
			if (doc == null || hostElement == null) return summary;

			List<Part> parts = this.GetLeafTilePartsForHost(doc, hostElement);
			if (parts.Count == 0) return summary;

			XYZ normal = _patOnlySketchPlane == null ? XYZ.BasisZ : _patOnlySketchPlane.Normal.Normalize();
			string roomLabel = string.IsNullOrWhiteSpace(_currentTileLayoutSpaceLabel) ? "未指定空間" : _currentTileLayoutSpaceLabel;
			Room currentRoom = _currentTileLayoutRoomId == ElementId.InvalidElementId ? null : doc.GetElement(_currentTileLayoutRoomId) as Room;
			Dictionary<ElementId, double> partAreas = new Dictionary<ElementId, double>();
			foreach (Part part in parts)
			{
				if (this.IsPartExcludedByMaker(part)) continue;
				if (!this.IsPartOnSelectedTilePlane(part, _patOnlySketchPlane))
				{
					this.ClearTileQuantityTagsForRoom(part, roomLabel);
					continue;
				}
				if (currentRoom != null && !this.IsTilePartInRoom(doc, part, currentRoom))
				{
					this.ClearTileQuantityTagsForRoom(part, roomLabel);
					continue;
				}
				double area = this.GetPartFaceAreaForQuantity(part, normal);
				if (area < 0.0001) continue;
				partAreas[part.Id] = area;
			}

			if (partAreas.Count == 0) return summary;

			double fullTileArea = this.GetExpectedFullTileAreaSqFt(partAreas.Values.ToList());
			double fullTolerance = fullTileArea > 0.0001 ? fullTileArea * 0.08 : 0.0;
			double surfaceAreaSqm = this.GetHostTileFaceAreaSquareMeters(hostElement, normal, partAreas.Values.Sum());
			string tileSurface = this.GetTileSurfaceLabel(hostElement);
			double tileWidthMm = _patOnlyTileParams == null ? this.ViewModel.TileWidth : _patOnlyTileParams.TileWidth;
			double tileHeightMm = _patOnlyTileParams == null ? this.ViewModel.TileHeight : _patOnlyTileParams.TileHeight;
			double tileThicknessMm = _patOnlyTileParams == null ? this.ViewModel.TileThickness : _patOnlyTileParams.Thickness;
			string tileSizeLabel = this.GetTileSizeLabel(tileWidthMm, tileHeightMm);
			int index = 1;
			foreach (Part part in parts)
			{
				if (!partAreas.TryGetValue(part.Id, out double areaSqFt)) continue;
				bool isFull = fullTileArea > 0.0001 && Math.Abs(areaSqFt - fullTileArea) <= fullTolerance;
				double areaRatio = fullTileArea > 0.0001 ? areaSqFt / fullTileArea : 0.0;
				bool isHalfCut = !isFull && areaRatio >= 0.5;
				bool isUnderHalfCut = !isFull && areaRatio < 0.5;
				string tileType = isFull ? "Full" : (isHalfCut ? "HalfCut" : "UnderHalfCut");
				double areaSqm = areaSqFt * 0.09290304;

				summary.Total++;
				if (!summary.SurfaceTotals.ContainsKey(tileSurface)) summary.SurfaceTotals[tileSurface] = 0;
				summary.SurfaceTotals[tileSurface]++;
				if (isFull) summary.Full++;
				else
				{
					summary.Cut++;
					if (isHalfCut) summary.HalfCut++;
					else if (isUnderHalfCut) summary.UnderHalfCut++;
				}
				summary.AreaSqm += areaSqm;

				this.SetSharedParameter(part, "Tile_Type", tileType);
				this.SetSharedParameter(part, "Tile_Width", tileWidthMm / 304.8);
				this.SetSharedParameter(part, "Tile_Height", tileHeightMm / 304.8);
				this.SetSharedParameter(part, "Tile_Thickness", tileThicknessMm / 304.8);
				this.SetSharedParameter(part, "Tile_Material", this.GetPluginOwnedMaterialName(_patOnlyTileParams));
				this.SetSharedParameter(part, "Tile_Surface", tileSurface);
				this.SetSharedParameter(part, "Room_ID", roomLabel);
				this.SetSharedParameter(part, "Anchor_ID", hostElement.Id.IntegerValue.ToString());
				this.SetSharedParameter(part, "Tile_Size", tileSizeLabel);
				this.NormalizeTileScheduleFieldsForPart(part);
				this.SetTileWasteParameters(part);

				Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
				if (commentParam != null && !commentParam.IsReadOnly)
				{
					string comment = string.Join("|", new[]
					{
						"Tile_ID:Part_" + hostElement.Id.IntegerValue + "_" + index.ToString("0000"),
						"Anchor_ID:" + hostElement.Id.IntegerValue.ToString(),
						"Room_ID:" + roomLabel,
						"Surface_ID:" + hostElement.Id.IntegerValue.ToString(),
						"Type:" + tileType,
						"Surface:" + tileSurface,
						"Size:" + tileSizeLabel,
						"Area:" + areaSqm.ToString("F6"),
						"Surface_Area:" + surfaceAreaSqm.ToString("F6", CultureInfo.InvariantCulture),
						"Area_Ratio:" + areaRatio.ToString("F3"),
						"Waste_Rate:" + this.GetTileWasteRatePercent().ToString("0.##") + "%",
						"Host_ID:" + hostElement.Id.IntegerValue.ToString()
					});
					commentParam.Set(comment);
				}

				index++;
			}

			this.ApplyAutomaticWasteRatesForParts(parts.Where(part => part != null && partAreas.ContainsKey(part.Id)).ToList());
			summary.FullTileAreaSqm = fullTileArea * 0.09290304;
			return summary;
		}

		private List<Part> GetLeafTilePartsForHost(Document doc, Element hostElement)
		{
			List<Part> result = new List<Part>();
			if (doc == null || hostElement == null || !PartUtils.HasAssociatedParts(doc, hostElement.Id)) return result;

			ICollection<ElementId> ids = PartUtils.GetAssociatedParts(doc, hostElement.Id, false, true);
			if (ids == null || ids.Count == 0)
			{
				ids = PartUtils.GetAssociatedParts(doc, hostElement.Id, true, true);
			}

			foreach (ElementId id in ids ?? new List<ElementId>())
			{
				Part part = doc.GetElement(id) as Part;
				if (part != null && part.IsValidObject)
				{
					result.Add(part);
				}
			}

			return result;
		}

		private string GetTileSurfaceLabel(Element hostElement)
		{
			if (hostElement == null) return "未分類";

			try
			{
				if (hostElement is Wall) return "牆面";
				if (hostElement is Ceiling || (hostElement.Category != null && hostElement.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Ceilings)) return "天花板";
				if (hostElement.Category != null && hostElement.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Roofs) return "天花板";
				if (hostElement is Floor) return "地坪";

				if (hostElement.Category != null)
				{
					BuiltInCategory category = (BuiltInCategory)hostElement.Category.Id.IntegerValue;
					if (category == BuiltInCategory.OST_Walls) return "牆面";
					if (category == BuiltInCategory.OST_Floors) return "地坪";
					if (category == BuiltInCategory.OST_Ceilings || category == BuiltInCategory.OST_Roofs) return "天花板";
				}
			}
			catch { }

			return "未分類";
		}

		private bool IsPartExcludedByMaker(Part part)
		{
			try
			{
				Parameter parameter = part.get_Parameter((BuiltInParameter)(-1152344));
				return parameter != null && parameter.HasValue && parameter.AsInteger() == 1;
			}
			catch { return false; }
		}

		private bool IsPartOnSelectedTilePlane(Part part, Plane selectedPlane)
		{
			if (part == null || selectedPlane == null) return true;
			try
			{
				XYZ targetNormal = selectedPlane.Normal.Normalize();
				const double planeTolerance = 0.005;
				Options options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
				GeometryElement geometry = part.get_Geometry(options);
				foreach (GeometryObject obj in geometry ?? Enumerable.Empty<GeometryObject>())
				{
					Solid solid = obj as Solid;
					if (solid == null || solid.Faces == null) continue;
					foreach (Face rawFace in solid.Faces)
					{
						PlanarFace planarFace = rawFace as PlanarFace;
						if (planarFace == null || planarFace.Area < 0.0001) continue;
						double parallel = Math.Abs(planarFace.FaceNormal.Normalize().DotProduct(targetNormal));
						if (parallel < 0.98) continue;
						double distance = Math.Abs((planarFace.Origin - selectedPlane.Origin).DotProduct(targetNormal));
						if (distance <= planeTolerance) return true;
					}
				}
			}
			catch { }
			return false;
		}

		private void ClearTileQuantityTagsForRoom(Part part, string roomLabel)
		{
			if (part == null || string.IsNullOrWhiteSpace(roomLabel)) return;
			Parameter commentParam = part.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
			string comments = commentParam == null ? null : commentParam.AsString();
			Parameter roomParam = part.LookupParameter("Room_ID");
			string currentRoom = roomParam != null && roomParam.HasValue ? roomParam.AsString() : this.ParseCommentValue(comments, "Room_ID");
			if (!string.Equals(currentRoom, roomLabel, StringComparison.OrdinalIgnoreCase)) return;

			this.SetSharedParameter(part, "Room_ID", string.Empty);
			this.SetSharedParameter(part, "Tile_Type", string.Empty);
			this.SetSharedParameter(part, "Tile_Surface", string.Empty);
			this.SetSharedParameter(part, "Tile_Size", string.Empty);
			this.SetSharedParameter(part, "Tile_Material", string.Empty);
			this.SetSharedParameter(part, "Tile_WasteRate", string.Empty);
			this.SetSharedParameter(part, "Tile_NetQty", 0.0);
			this.SetSharedParameter(part, "Tile_QtyWithWaste", 0.0);
			this.SetSharedParameter(part, "Tile_AreaM2", 0.0);
			this.SetSharedParameter(part, "Tile_CalcMethod", string.Empty);
			this.SetSharedParameter(part, "Tile_SurfaceAreaText", string.Empty);
			this.SetSharedParameter(part, "Tile_NetQtyText", string.Empty);
			this.SetSharedParameter(part, "Tile_PurchaseQtyText", string.Empty);
			this.SetSharedParameter(part, "Tile_WasteQtyText", string.Empty);
			this.SetSharedParameter(part, "Tile_TotalNetQtyText", string.Empty);
			this.SetSharedParameter(part, "Tile_TotalPurchaseQtyText", string.Empty);
			this.SetSharedParameter(part, "Tile_PurchaseCalcMethod", string.Empty);

			if (commentParam != null && !commentParam.IsReadOnly)
			{
				string cleaned = this.RemoveCommentValues(
					comments,
					"Tile_ID",
					"Anchor_ID",
					"Room_ID",
					"Surface_ID",
					"Type",
					"Surface",
					"Size",
					"Area",
					"Surface_Area",
					"Area_Ratio",
					"Waste_Rate",
					"Net_Qty",
					"Purchase_Qty",
					"Waste_Qty",
					"Calc_Method",
					"Host_ID");
				commentParam.Set(cleaned);
			}
		}

		private string RemoveCommentValues(string comments, params string[] keys)
		{
			if (string.IsNullOrWhiteSpace(comments)) return string.Empty;
			HashSet<string> keySet = new HashSet<string>(keys ?? new string[0], StringComparer.OrdinalIgnoreCase);
			List<string> kept = comments
				.Split('|')
				.Where(part =>
				{
					if (string.IsNullOrWhiteSpace(part)) return false;
					int colon = part.IndexOf(':');
					if (colon <= 0) return true;
					string key = part.Substring(0, colon);
					return !keySet.Contains(key);
				})
				.ToList();
			return string.Join("|", kept);
		}

		private double GetExpectedFullTileAreaSqFt(IList<double> actualAreas)
		{
			if (_patOnlyTileParams != null && _patOnlyTileParams.TileWidth > 0.1 && _patOnlyTileParams.TileHeight > 0.1)
			{
				return (_patOnlyTileParams.TileWidth / 304.8) * (_patOnlyTileParams.TileHeight / 304.8);
			}

			return actualAreas == null || actualAreas.Count == 0 ? 0.0 : actualAreas.Max();
		}

		private double GetPartFaceAreaForQuantity(Part part, XYZ targetNormal)
		{
			double bestArea = 0.0;
			try
			{
				Options options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
				GeometryElement geometry = part.get_Geometry(options);
				if (geometry != null)
				{
					foreach (GeometryObject obj in geometry)
					{
						Solid solid = obj as Solid;
						if (solid == null || solid.Faces == null) continue;
						foreach (Face rawFace in solid.Faces)
						{
							PlanarFace planarFace = rawFace as PlanarFace;
							if (planarFace == null) continue;
							double parallel = Math.Abs(planarFace.FaceNormal.Normalize().DotProduct(targetNormal));
							if (parallel < 0.98) continue;
							if (planarFace.Area > bestArea) bestArea = planarFace.Area;
						}
					}
				}
			}
			catch { }

			if (bestArea > 0.0001) return bestArea;

			try
			{
				Parameter areaParam = part.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
				if (areaParam != null && areaParam.HasValue) return areaParam.AsDouble();
			}
			catch { }

			return 0.0;
		}

		private double GetHostTileFaceAreaSquareMeters(Element hostElement, XYZ targetNormal, double fallbackAreaSqFt)
		{
			double bestAreaSqFt = 0.0;
			try
			{
				Options options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine };
				GeometryElement geometry = hostElement == null ? null : hostElement.get_Geometry(options);
				if (geometry != null)
				{
					foreach (GeometryObject obj in geometry)
					{
						this.CollectBestHostFaceArea(obj, targetNormal, ref bestAreaSqFt);
					}
				}
			}
			catch { }

			double areaSqFt = bestAreaSqFt > 0.0001 ? bestAreaSqFt : fallbackAreaSqFt;
			return areaSqFt * 0.09290304;
		}

		private void CollectBestHostFaceArea(GeometryObject obj, XYZ targetNormal, ref double bestAreaSqFt)
		{
			if (obj == null || targetNormal == null) return;
			GeometryInstance instance = obj as GeometryInstance;
			if (instance != null)
			{
				try
				{
					foreach (GeometryObject nested in instance.GetInstanceGeometry())
					{
						this.CollectBestHostFaceArea(nested, targetNormal, ref bestAreaSqFt);
					}
				}
				catch { }
				return;
			}

			Solid solid = obj as Solid;
			if (solid == null || solid.Faces == null || solid.Volume < 0.000001) return;
			foreach (Face rawFace in solid.Faces)
			{
				PlanarFace planarFace = rawFace as PlanarFace;
				if (planarFace == null) continue;
				double parallel = Math.Abs(planarFace.FaceNormal.Normalize().DotProduct(targetNormal));
				if (parallel < 0.98) continue;
				if (planarFace.Area > bestAreaSqFt) bestAreaSqFt = planarFace.Area;
			}
		}

		private List<Curve> GetCapturedPatCurvesFromModelCurves(Document doc)
		{
			return this.GetCapturedPatCurvesFromModelCurves(doc, _patOnlyPreviewLineIds);
		}

		private List<Curve> GetCapturedPatCurvesFromModelCurves(Document doc, IEnumerable<ElementId> lineIds)
		{
			List<Curve> curves = new List<Curve>();
			foreach (ElementId id in (lineIds ?? new List<ElementId>()).ToList())
			{
				ModelCurve modelCurve = doc.GetElement(id) as ModelCurve;
				if (modelCurve == null)
				{
					continue;
				}

				Curve curve = modelCurve.GeometryCurve;
				if (curve != null)
				{
					curves.Add(curve.Clone());
				}
			}
			return curves;
		}

		private void ApplyCapturedPatLineStyle(Document doc, ModelCurve modelCurve)
		{
			try
			{
				Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
				Category patCategory = linesCategory.SubCategories
					.Cast<Category>()
					.FirstOrDefault(c => string.Equals(c.Name, "TilePAT_Captured", StringComparison.OrdinalIgnoreCase));

				if (patCategory == null)
				{
					patCategory = doc.Settings.Categories.NewSubcategory(linesCategory, "TilePAT_Captured");
				}
				patCategory.LineColor = new Color(255, 0, 0);
				patCategory.SetLineWeight(6, GraphicsStyleType.Projection);

				GraphicsStyle style = patCategory.GetGraphicsStyle(GraphicsStyleType.Projection);
				if (style != null)
				{
					modelCurve.LineStyle = style;
				}
			}
			catch { }
		}

		private void ApplyPatBoundaryLineStyle(Document doc, ModelCurve modelCurve)
		{
			try
			{
				Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
				Category boundaryCategory = linesCategory.SubCategories
					.Cast<Category>()
					.FirstOrDefault(c => string.Equals(c.Name, "TilePAT_Boundary", StringComparison.OrdinalIgnoreCase));

				if (boundaryCategory == null)
				{
					boundaryCategory = doc.Settings.Categories.NewSubcategory(linesCategory, "TilePAT_Boundary");
				}
				boundaryCategory.LineColor = new Color(255, 170, 0);
				boundaryCategory.SetLineWeight(10, GraphicsStyleType.Projection);

				GraphicsStyle style = boundaryCategory.GetGraphicsStyle(GraphicsStyleType.Projection);
				if (style != null)
				{
					modelCurve.LineStyle = style;
				}
			}
			catch { }
		}

		private void ApplyTileSetoutLineStyle(Document doc, ModelCurve modelCurve)
		{
			try
			{
				Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
				Category setoutCategory = linesCategory.SubCategories
					.Cast<Category>()
					.FirstOrDefault(c => string.Equals(c.Name, "TilePAT_Setout", StringComparison.OrdinalIgnoreCase));

				if (setoutCategory == null)
				{
					setoutCategory = doc.Settings.Categories.NewSubcategory(linesCategory, "TilePAT_Setout");
				}
				setoutCategory.LineColor = new Color(0, 220, 220);
				setoutCategory.SetLineWeight(12, GraphicsStyleType.Projection);

				GraphicsStyle style = setoutCategory.GetGraphicsStyle(GraphicsStyleType.Projection);
				if (style != null)
				{
					modelCurve.LineStyle = style;
				}
			}
			catch { }
		}

		private void DoConfirmCalibrationAndCreateParts(UIDocument uidoc, Document doc)
		{
			Reference reference;
			Element hostElement;
			PlanarFace face;
			if (!this.TryPickPatFace(uidoc, doc, out reference, out hostElement, out face, "Select the tiled face to divide by its PAT only.")) return;
			List<Curve> curves;
			if (!this.TryGetPatOnlyCurves(uidoc, doc, reference, hostElement, face, out curves)) return;
			_patOnlyHostElement = hostElement;
			_patOnlySketchPlane = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
			_patOnlySplitCurves = curves;
			using (Transaction t = new Transaction(doc, "Create and divide Parts by PAT only"))
			{
				t.Start();
				SharedParameterHelper.RegisterAndBindParameters(doc);
				ICollection<ElementId> parts = this.RecreatePatOnlyHostParts(doc, hostElement);
				if (!PartUtils.ArePartsValidForDivide(doc, parts))
				{
					TaskDialog.Show("Tile Parts", "建立出的 Parts 無法執行 Divide Parts。請確認選到的是牆或樓板的可分割面。");
					t.RollBack();
					return;
				}
				SketchPlane sketchPlane = SketchPlane.Create(doc, _patOnlySketchPlane);
				PartMaker divisionMaker = PartUtils.DivideParts(doc, parts, new List<ElementId>(), curves, sketchPlane.Id);
				foreach (ElementId id in _patOnlyPreviewLineIds) { try { doc.Delete(id); } catch { } }
				_patOnlyPreviewLineIds.Clear();
				this.EnablePatOnlyPartsViews(doc, uidoc.ActiveView);
				doc.Regenerate();
				if (!this.VerifyPartsHaveSplittingCurves(doc, hostElement, divisionMaker))
				{
					TaskDialog.Show("Tile Parts", "Divide Parts 已呼叫，但 Revit 沒有產生可驗證的分割草圖曲線。");
				}
				t.Commit();
			}
			Element partMakerElement = PartUtils.GetAssociatedPartMaker(doc, hostElement.Id);
			if (partMakerElement != null) uidoc.Selection.SetElementIds(new List<ElementId> { partMakerElement.Id });
		}

		private bool VerifyPartsHaveSplittingCurves(Document doc, Element hostElement, PartMaker maker)
		{
			if (doc == null || hostElement == null || maker == null) return false;
			try
			{
				ICollection<ElementId> partIds = PartUtils.GetAssociatedParts(doc, hostElement.Id, true, true);
				foreach (ElementId partId in partIds)
				{
					Plane plane;
					IList<Curve> splitCurves = PartUtils.GetSplittingCurves(doc, partId, out plane);
					if (splitCurves != null && splitCurves.Count > 0) return true;
				}
			}
			catch { }
			return false;
		}

		private void DoDeleteTileParts(UIDocument uidoc, Document doc)
		{
			List<Element> hostElements = this.GetSelectedTileHostElements(doc, uidoc.Selection.GetElementIds());
			if (hostElements.Count == 0 && _patOnlyHostElement != null && _patOnlyHostElement.IsValidObject)
			{
				hostElements.Add(_patOnlyHostElement);
			}
			if (hostElements.Count == 0)
			{
				Reference reference;
				Element hostElement;
				PlanarFace face;
				if (!this.TryPickPatFace(uidoc, doc, out reference, out hostElement, out face, "Select tile host to delete Parts.")) return;
				if (hostElement != null) hostElements.Add(hostElement);
			}

			int deletedMakers = 0;
			int deletedParts = 0;
			int deletedPreviewLines = 0;
			using (Transaction t = new Transaction(doc, "Delete tile Parts"))
			{
				t.Start();
				foreach (Element hostElement in hostElements
					.Where(element => element != null)
					.GroupBy(element => element.Id.IntegerValue)
					.Select(group => group.First()))
				{
					try
					{
						if (PartUtils.HasAssociatedParts(doc, hostElement.Id))
						{
							Element maker = PartUtils.GetAssociatedPartMaker(doc, hostElement.Id);
							if (maker != null)
							{
								doc.Delete(maker.Id);
								deletedMakers++;
								doc.Regenerate();
							}

							ICollection<ElementId> partIds = PartUtils.GetAssociatedParts(doc, hostElement.Id, true, true);
							foreach (ElementId partId in partIds ?? new List<ElementId>())
							{
								try
								{
									doc.Delete(partId);
									deletedParts++;
								}
								catch { }
							}
						}
					}
					catch { }
				}
				foreach (ElementId id in _patOnlyPreviewLineIds) { try { doc.Delete(id); deletedPreviewLines++; } catch { } }
				foreach (ElementId id in _patOnlyBoundaryPreviewLineIds) { try { doc.Delete(id); deletedPreviewLines++; } catch { } }
				_patOnlyPreviewLineIds.Clear();
				_patOnlyBoundaryPreviewLineIds.Clear();
				_tileLayoutPreviewSets.Clear();
				_patOnlyHostElement = null;
				t.Commit();
			}
			uidoc.Selection.SetElementIds(new List<ElementId>());
			uidoc.RefreshActiveView();
			this.ViewModel.SetStatus(string.Format("刪除 Parts 完成：宿主 {0} 個，PartMaker {1} 個，Parts {2} 個，控制線 {3} 條。", hostElements.Count, deletedMakers, deletedParts, deletedPreviewLines));
		}

		private void DoEditPartsSketch(UIApplication app, UIDocument uidoc, Document doc)
		{
			Element hostElement = _patOnlyHostElement;
			if (hostElement == null || !hostElement.IsValidObject)
			{
				List<Element> selectedHosts = this.GetSelectedTileHostElements(doc, uidoc.Selection.GetElementIds());
				hostElement = selectedHosts.FirstOrDefault();
				if (hostElement == null)
				{
					Reference reference;
					PlanarFace face;
					if (!this.TryPickPatFace(uidoc, doc, out reference, out hostElement, out face, "請選取要編輯 Parts 分割草圖的磁磚面。")) return;
				}
				_patOnlyHostElement = hostElement;
			}

			if (!PartUtils.HasAssociatedParts(doc, hostElement.Id))
			{
				using (Transaction t = new Transaction(doc, "Create Parts before editing sketch"))
				{
					t.Start();
					SharedParameterHelper.RegisterAndBindParameters(doc);
					ICollection<ElementId> parts = this.RecreatePatOnlyHostParts(doc, hostElement);
					this.EnablePatOnlyPartsViews(doc, uidoc.ActiveView);
					t.Commit();
					if (parts != null && parts.Count > 0) uidoc.Selection.SetElementIds(parts);
				}

				if (!PartUtils.HasAssociatedParts(doc, hostElement.Id))
				{
					TaskDialog.Show("Tile Parts", "無法替這個宿主建立 Parts。請確認選到的是可建立 Parts 的牆、樓板或可分割宿主面。");
					return;
				}
			}

			Element maker = PartUtils.GetAssociatedPartMaker(doc, hostElement.Id);
			if (maker == null)
			{
				TaskDialog.Show("Tile Parts", "找不到這個宿主的 PartMaker，無法開啟 Parts 分割草圖。");
				return;
			}

			uidoc.Selection.SetElementIds(new List<ElementId> { maker.Id });
			uidoc.RefreshActiveView();

			string[] commandIds = new[]
			{
				"ID_EDIT_SKETCH",
				"ID_EDIT_PART_DIVISION_SKETCH",
				"ID_EDIT_PROFILE"
			};
			foreach (string commandId in commandIds)
			{
				try
				{
					RevitCommandId revitCommandId = RevitCommandId.LookupCommandId(commandId);
					if (revitCommandId != null && app.CanPostCommand(revitCommandId))
					{
						app.PostCommand(revitCommandId);
						this.ViewModel.SetStatus("已選取 Parts 分割草圖，正在開啟編輯。");
						return;
					}
				}
				catch { }
			}

			this.ViewModel.SetStatus("已選取 Parts 分割草圖。請在 Revit 按 Edit Sketch / Edit Division。");
			TaskDialog.Show("Tile Parts", "已選取 PartMaker，但目前 Revit 版本沒有開放可由外掛直接呼叫的 Edit Sketch 指令。請接著按 Revit 介面上的 Edit Sketch / Edit Division。");
		}

		private List<Element> GetSelectedTileHostElements(Document doc, ICollection<ElementId> selectedIds)
		{
			List<Element> hosts = new List<Element>();
			if (doc == null || selectedIds == null) return hosts;

			foreach (ElementId id in selectedIds)
			{
				Element element = doc.GetElement(id);
				if (element == null) continue;
				Element host = this.ResolvePatOnlyPartsHost(doc, element);
				if (this.IsTileHostCandidate(host))
				{
					hosts.Add(host);
				}
			}

			return hosts
				.Where(element => element != null)
				.GroupBy(element => element.Id.IntegerValue)
				.Select(group => group.First())
				.ToList();
		}

		private void DoOpenWallJoinTool(UIApplication app, UIDocument uidoc, Document doc)
		{
			try
			{
				List<Wall> walls = uidoc.Selection.GetElementIds()
					.Select(id => doc.GetElement(id))
					.OfType<Wall>()
					.Where(wall => wall != null && wall.IsValidObject)
					.GroupBy(wall => wall.Id.IntegerValue)
					.Select(group => group.First())
					.ToList();

				if (walls.Count == 0)
				{
					IList<Reference> references;
					try
					{
						references = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectionFilter(), "Select walls to batch update wall joins.");
					}
					catch (Autodesk.Revit.Exceptions.OperationCanceledException)
					{
						this.DoOpenWallJoinTool(app, uidoc);
						return;
					}

					walls = references
						.Select(reference => doc.GetElement(reference.ElementId))
						.OfType<Wall>()
						.Where(wall => wall != null && wall.IsValidObject)
						.GroupBy(wall => wall.Id.IntegerValue)
						.Select(group => group.First())
						.ToList();
				}

				if (walls.Count == 0)
				{
					TaskDialog.Show("Tile Parts", "Please select one or more walls before using batch wall join.");
					return;
				}

				TileBoundaryJoinMode mode = this.GetSelectedBoundaryJoinMode();
				string modeLabel = this.GetBoundaryJoinModeLabel(mode);
				bool shouldJoin = mode == TileBoundaryJoinMode.OuterCornerWrap || mode == TileBoundaryJoinMode.MiterJoin;
				bool shouldMiter = mode == TileBoundaryJoinMode.MiterJoin || mode == TileBoundaryJoinMode.OuterCornerWrap;
				int changedEnds = 0;
				int joinedPairs = 0;
				int unjoinedPairs = 0;
				int miterEnds = 0;
				Dictionary<ElementId, HashSet<int>> connectedEnds = this.GetConnectedWallEnds(walls, 0.20);

				using (Transaction transaction = new Transaction(doc, "Batch wall join mode"))
				{
					transaction.Start();
					foreach (Wall wall in walls)
					{
						HashSet<int> ends;
						if (!connectedEnds.TryGetValue(wall.Id, out ends) || ends.Count == 0) continue;
						foreach (int end in ends)
						{
							try
							{
								if (shouldJoin)
								{
									WallUtils.AllowWallJoinAtEnd(wall, end);
								}
								else
								{
									WallUtils.DisallowWallJoinAtEnd(wall, end);
									this.TrySetWallJoinType(wall, end, "Butt");
								}
								changedEnds++;
							}
							catch { }
						}
					}

					for (int i = 0; i < walls.Count; i++)
					{
						for (int j = i + 1; j < walls.Count; j++)
						{
							Wall first = walls[i];
							Wall second = walls[j];
							if (!this.DoWallsTouch(first, second, 0.20)) continue;

							try
							{
								bool areJoined = JoinGeometryUtils.AreElementsJoined(doc, first, second);
								if (shouldJoin && !areJoined)
								{
									JoinGeometryUtils.JoinGeometry(doc, first, second);
									joinedPairs++;
								}
								else if (!shouldJoin && areJoined)
								{
									JoinGeometryUtils.UnjoinGeometry(doc, first, second);
									unjoinedPairs++;
								}
							}
							catch { }
						}
					}
					if (shouldMiter)
					{
						doc.Regenerate();
						foreach (Wall wall in walls)
						{
							HashSet<int> ends;
							if (!connectedEnds.TryGetValue(wall.Id, out ends) || ends.Count == 0) continue;
							foreach (int end in ends)
							{
								if (this.TrySetWallJoinType(wall, end, "Miter")) miterEnds++;
							}
						}
					}
					transaction.Commit();
				}

				uidoc.Selection.SetElementIds(walls.Select(wall => wall.Id).ToList());
				uidoc.RefreshActiveView();
				string apiNote = shouldMiter && miterEnds == 0
					? "；注意：此 Revit API 版本未公開斜角型式設定，已完成牆端 Join，必要時請用原生 Wall Joins 確認接合型式。"
					: string.Empty;
				this.ViewModel.SetStatus(string.Format("批次牆接合完成：{0} 道牆，方式 [{1}]，處理交界端 {2}，Join {3}，Unjoin {4}，斜角端 {5}{6}", walls.Count, modeLabel, changedEnds, joinedPairs, unjoinedPairs, miterEnds, apiNote));
			}
			catch (Exception ex)
			{
				TaskDialog.Show("Tile Parts", ex.ToString());
				this.ViewModel.SetStatus("Batch wall join failed: " + ex.Message);
			}
		}

		private Dictionary<ElementId, HashSet<int>> GetConnectedWallEnds(IList<Wall> walls, double tolerance)
		{
			Dictionary<ElementId, HashSet<int>> result = new Dictionary<ElementId, HashSet<int>>();
			foreach (Wall wall in walls ?? new List<Wall>())
			{
				if (wall == null) continue;
				result[wall.Id] = new HashSet<int>();
			}

			for (int i = 0; i < (walls == null ? 0 : walls.Count); i++)
			{
				Wall first = walls[i];
				Curve firstCurve = this.GetWallLocationCurve(first);
				if (firstCurve == null) continue;
				for (int j = i + 1; j < walls.Count; j++)
				{
					Wall second = walls[j];
					Curve secondCurve = this.GetWallLocationCurve(second);
					if (secondCurve == null) continue;
					for (int firstEnd = 0; firstEnd < 2; firstEnd++)
					{
						XYZ firstPoint = firstCurve.GetEndPoint(firstEnd);
						if (this.DistancePointToCurve(firstPoint, secondCurve) <= tolerance)
						{
							result[first.Id].Add(firstEnd);
						}
					}
					for (int secondEnd = 0; secondEnd < 2; secondEnd++)
					{
						XYZ secondPoint = secondCurve.GetEndPoint(secondEnd);
						if (this.DistancePointToCurve(secondPoint, firstCurve) <= tolerance)
						{
							result[second.Id].Add(secondEnd);
						}
					}
				}
			}

			return result;
		}

		private Curve GetWallLocationCurve(Wall wall)
		{
			try
			{
				LocationCurve locationCurve = wall == null ? null : wall.Location as LocationCurve;
				return locationCurve == null ? null : locationCurve.Curve;
			}
			catch
			{
				return null;
			}
		}

		private bool DoWallsTouch(Wall first, Wall second, double tolerance)
		{
			Curve firstCurve = this.GetWallLocationCurve(first);
			Curve secondCurve = this.GetWallLocationCurve(second);
			if (firstCurve == null || secondCurve == null) return this.DoElementBoundingBoxesTouch(first, second, tolerance);
			for (int end = 0; end < 2; end++)
			{
				if (this.DistancePointToCurve(firstCurve.GetEndPoint(end), secondCurve) <= tolerance) return true;
				if (this.DistancePointToCurve(secondCurve.GetEndPoint(end), firstCurve) <= tolerance) return true;
			}
			return this.DoElementBoundingBoxesTouch(first, second, tolerance * 0.5);
		}

		private double DistancePointToCurve(XYZ point, Curve curve)
		{
			if (point == null || curve == null) return double.MaxValue;
			try
			{
				IntersectionResult result = curve.Project(point);
				if (result != null && result.XYZPoint != null) return point.DistanceTo(result.XYZPoint);
			}
			catch { }
			try
			{
				return Math.Min(point.DistanceTo(curve.GetEndPoint(0)), point.DistanceTo(curve.GetEndPoint(1)));
			}
			catch
			{
				return double.MaxValue;
			}
		}

		private bool TrySetWallJoinType(Wall wall, int end, string joinTypeName)
		{
			if (wall == null || string.IsNullOrWhiteSpace(joinTypeName)) return false;
			try
			{
				LocationCurve locationCurve = wall.Location as LocationCurve;
				if (locationCurve == null) return false;

				string normalized = string.Equals(joinTypeName, "Butt", StringComparison.OrdinalIgnoreCase)
					? "Abut"
					: joinTypeName;
				JoinType joinType;
				if (!Enum.TryParse(normalized, true, out joinType)) return false;

				if (locationCurve.get_JoinType(end) == joinType) return true;
				locationCurve.set_JoinType(end, joinType);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private bool DoElementBoundingBoxesTouch(Element first, Element second, double tolerance)
		{
			if (first == null || second == null) return false;
			try
			{
				BoundingBoxXYZ firstBox = first.get_BoundingBox(null);
				BoundingBoxXYZ secondBox = second.get_BoundingBox(null);
				if (firstBox == null || secondBox == null) return false;

				return firstBox.Min.X <= secondBox.Max.X + tolerance &&
					firstBox.Max.X + tolerance >= secondBox.Min.X &&
					firstBox.Min.Y <= secondBox.Max.Y + tolerance &&
					firstBox.Max.Y + tolerance >= secondBox.Min.Y &&
					firstBox.Min.Z <= secondBox.Max.Z + tolerance &&
					firstBox.Max.Z + tolerance >= secondBox.Min.Z;
			}
			catch
			{
				return false;
			}
		}

		private void DoOpenWallJoinTool(UIApplication app, UIDocument uidoc)
		{
			try
			{
				RevitCommandId commandId = RevitCommandId.LookupPostableCommandId(PostableCommand.WallJoins);
				if (commandId != null && app.CanPostCommand(commandId))
				{
					app.PostCommand(commandId);
					this.ViewModel.SetStatus("已開啟 Revit 牆接合工具。請在模型中選取牆端並切換接合方式。");
					return;
				}
			}
			catch { }

			TaskDialog.Show("Tile Parts", "目前 Revit 無法由外掛切換到 Wall Joins 工具。請手動到 Modify > Geometry > Wall Joins 開啟。");
		}

		public TileSyncEventHandler()
		{
			this.CurrentOperation = TileSyncEventHandler.Operation.None;
			this._lastCreatedTileIds = new List<ElementId>();
			this.createdTileIds = new List<ElementId>();
		}
	}
}

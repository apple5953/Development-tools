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
			AddJointParamToWalls,
			AddJointParamToFloors,
			DeleteTiles,
			CreateSchedule,
			ExportExcel,
			ConvertToEditable,
			None
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
					this.DoCreateSchedule(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.ExportExcel:
					this.DoExportExcel(activeUIDocument, document);
					break;
				case TileSyncEventHandler.Operation.ConvertToEditable:
					this.DoConvertToEditable(app, activeUIDocument, document);
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
												bool flag9 = parameter.AsDouble() < 1E-06;
												if (flag9)
												{
													parameter.Set(0.00984251968503937);
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
					streamWriter.WriteLine("PARAM\ta2b3c4d5-0001-4ebc-9999-tile0000001a\tTile_Joint_Width\tNUMBER\t\t1\t1\t磁磚縫隙寬度(mm)\t1");
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
					ViewSchedule viewSchedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
					viewSchedule.Name = "磁磚統計明細表_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
					IList<SchedulableField> schedulableFields = viewSchedule.Definition.GetSchedulableFields();
					ScheduleField scheduleField = null;
					ScheduleField scheduleField2 = null;
					ScheduleField scheduleField3 = null;
					ScheduleField scheduleField4 = null;
					ScheduleField scheduleField5 = null;
					ScheduleField scheduleField6 = null;
					foreach (SchedulableField current in schedulableFields)
					{
						bool flag = current.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
						if (flag)
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
							bool flag2 = name.Equals("Count", StringComparison.OrdinalIgnoreCase) || name.Equals("數量", StringComparison.OrdinalIgnoreCase) || name.Equals("合計", StringComparison.OrdinalIgnoreCase);
							if (flag2)
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
							else
							{
								bool flag3 = name.Equals("Room_ID", StringComparison.OrdinalIgnoreCase);
								if (flag3)
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
								else
								{
									bool flag4 = name.Equals("Tile_Material", StringComparison.OrdinalIgnoreCase);
									if (flag4)
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
									else
									{
										bool flag5 = name.Equals("Tile_Type", StringComparison.OrdinalIgnoreCase);
										if (flag5)
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
										else
										{
											bool flag6 = name.Equals("Tile_Width", StringComparison.OrdinalIgnoreCase);
											if (flag6)
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
											else
											{
												bool flag7 = name.Equals("Tile_Height", StringComparison.OrdinalIgnoreCase);
												if (flag7)
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
												else
												{
													bool flag8 = name.Equals("Tile_Thickness", StringComparison.OrdinalIgnoreCase);
													if (flag8)
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
										}
									}
								}
							}
						}
					}
					viewSchedule.Definition.IsItemized = false;
					viewSchedule.Definition.ShowGrandTotal = true;
					bool flag9 = scheduleField2 != null;
					if (flag9)
					{
						ScheduleSortGroupField scheduleSortGroupField = new ScheduleSortGroupField(scheduleField2.FieldId);
						scheduleSortGroupField.ShowHeader = true;
						scheduleSortGroupField.ShowFooter = true;
						viewSchedule.Definition.AddSortGroupField(scheduleSortGroupField);
					}
					bool flag10 = scheduleField3 != null;
					if (flag10)
					{
						ScheduleSortGroupField sortGroupField = new ScheduleSortGroupField(scheduleField3.FieldId);
						viewSchedule.Definition.AddSortGroupField(sortGroupField);
					}
					bool flag11 = scheduleField4 != null;
					if (flag11)
					{
						ScheduleSortGroupField sortGroupField2 = new ScheduleSortGroupField(scheduleField4.FieldId);
						viewSchedule.Definition.AddSortGroupField(sortGroupField2);
					}
					bool flag12 = scheduleField5 != null;
					if (flag12)
					{
						ScheduleSortGroupField sortGroupField3 = new ScheduleSortGroupField(scheduleField5.FieldId);
						viewSchedule.Definition.AddSortGroupField(sortGroupField3);
					}
					bool flag13 = scheduleField6 != null;
					if (flag13)
					{
						ScheduleSortGroupField sortGroupField4 = new ScheduleSortGroupField(scheduleField6.FieldId);
						viewSchedule.Definition.AddSortGroupField(sortGroupField4);
					}
					bool flag14 = scheduleField != null;
					if (flag14)
					{
						ScheduleFilter filter = new ScheduleFilter(scheduleField.FieldId, ScheduleFilterType.Contains, "Tile_ID:");
						viewSchedule.Definition.AddFilter(filter);
					}
					transaction.Commit();
					uidoc.ActiveView = viewSchedule;
				}
				this.ViewModel.SetStatus("建立 Revit 磁磚明細表完成。");
			}
			catch (Exception ex)
			{
				TaskDialog.Show("建立明細表錯誤", ex.ToString());
				this.ViewModel.SetStatus("建立明細表失敗: " + ex.Message);
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
		public TileSyncEventHandler()
		{
			this.CurrentOperation = TileSyncEventHandler.Operation.None;
			this._lastCreatedTileIds = new List<ElementId>();
			this.createdTileIds = new List<ElementId>();
		}
	}
}


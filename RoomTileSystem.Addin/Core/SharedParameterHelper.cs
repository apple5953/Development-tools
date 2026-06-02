using System;
using System.IO;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;

namespace RoomTileSystem.Core
{
    public static class SharedParameterHelper
    {
        private static readonly string[] ParameterNames = {
            "Anchor_ID", "Room_ID", "Main_Wall", "Tile_Width", "Tile_Height", 
            "Tile_Thickness", "Joint_Width", "Rotation_Angle", "Pattern_Type", 
            "Wall_Start_Height", "Wall_End_Height", "Floor_Tile_Type", "Wall_Tile_Type", 
            "Generate_Mode", "Preview_Mode", "Tile_Material", "Tile_RVT_Path", "Auto_Update",
            "Tile_Type"
        };

        private static readonly ForgeTypeId[] ParameterTypes = {
            SpecTypeId.String.Text,       // Anchor_ID
            SpecTypeId.String.Text,       // Room_ID
            SpecTypeId.String.Text,       // Main_Wall
            SpecTypeId.Length,            // Tile_Width
            SpecTypeId.Length,            // Tile_Height
            SpecTypeId.Length,            // Tile_Thickness
            SpecTypeId.Length,            // Joint_Width
            SpecTypeId.Angle,             // Rotation_Angle
            SpecTypeId.Int.Integer,       // Pattern_Type
            SpecTypeId.Length,            // Wall_Start_Height
            SpecTypeId.Length,            // Wall_End_Height
            SpecTypeId.String.Text,       // Floor_Tile_Type
            SpecTypeId.String.Text,       // Wall_Tile_Type
            SpecTypeId.Int.Integer,       // Generate_Mode
            SpecTypeId.Int.Integer,       // Preview_Mode
            SpecTypeId.String.Text,       // Tile_Material
            SpecTypeId.String.Text,       // Tile_RVT_Path
            SpecTypeId.Boolean.YesNo,     // Auto_Update
            SpecTypeId.String.Text        // Tile_Type
        };

        // 取得或建立臨時共享參數檔案
        private static DefinitionFile GetOrCreateSharedParameterFile(Application app)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "RoomTileSharedParameters.txt");
            
            // 建立定義檔內容 (Revit 共享參數檔標準編碼為 Unicode/UTF-16LE)
            using (StreamWriter sw = new StreamWriter(tempFile, false, System.Text.Encoding.Unicode))
            {
                sw.WriteLine("# This is a Revit shared parameter file.");
                sw.WriteLine("# Do not edit manually.");
                sw.WriteLine("*META\tVERSION\tMINVERSION");
                sw.WriteLine("META\t2\t1");
                sw.WriteLine("*GROUP\tID\tNAME");
                sw.WriteLine("GROUP\t1\tRoom Tile parameters");
                sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE");
                
                // 19 個參數的 GUID 是固定的，以便在不同專案和族群中保持一致性
                string[] guids = {
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3001", // Anchor_ID
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3002", // Room_ID
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3003", // Main_Wall
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3004", // Tile_Width
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3005", // Tile_Height
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3006", // Tile_Thickness
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3007", // Joint_Width
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3008", // Rotation_Angle
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3009", // Pattern_Type
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3010", // Wall_Start_Height
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3011", // Wall_End_Height
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3012", // Floor_Tile_Type
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3013", // Wall_Tile_Type
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3014", // Generate_Mode
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3015", // Preview_Mode
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3016", // Tile_Material
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3017", // Tile_RVT_Path
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3018", // Auto_Update
                    "b1a6c4b2-849c-4ebc-b3a6-5fa2147d3019"  // Tile_Type
                };

                for (int i = 0; i < ParameterNames.Length; i++)
                {
                    string typeStr = GetDataTypeString(ParameterTypes[i]);
                    sw.WriteLine($"PARAM\t{guids[i]}\t{ParameterNames[i]}\t{typeStr}\t\t1\t1\t\t1");
                }
            }

            app.SharedParametersFilename = tempFile;
            return app.OpenSharedParameterFile();
        }

        // 專案環境：綁定共享參數到專案的 Generic Models
        public static void RegisterAndBindParameters(Document doc)
        {
            if (doc.IsFamilyDocument) return;

            Application app = doc.Application;
            string oldPath = app.SharedParametersFilename;

            try
            {
                DefinitionFile defFile = GetOrCreateSharedParameterFile(app);
                if (defFile == null) return;

                DefinitionGroup group = defFile.Groups.get_Item("Room Tile parameters");
                if (group == null) return;

                Category genModelCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
                Category wallCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                Category floorCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Floors);
                CategorySet myCategories = app.Create.NewCategorySet();
                myCategories.Insert(genModelCategory);
                if (wallCategory != null) myCategories.Insert(wallCategory);
                if (floorCategory != null) myCategories.Insert(floorCategory);

                using (Transaction t = new Transaction(doc, "Register Shared Parameters"))
                {
                    t.Start();

                    foreach (Definition def in group.Definitions)
                    {
                        BindingMap map = doc.ParameterBindings;
                        if (map.Contains(def)) continue;

                        InstanceBinding instanceBinding = app.Create.NewInstanceBinding(myCategories);
                        
                        // Revit 2024 參數群組使用 GroupTypeId
                        doc.ParameterBindings.Insert(def, instanceBinding, GroupTypeId.Data);
                    }

                    t.Commit();
                }
            }
            finally
            {
                app.SharedParametersFilename = oldPath;
            }
        }

        // 族群編輯環境：直接將共享參數寫入目前的 RFA 檔案中
        public static void AddParametersToFamily(Document familyDoc)
        {
            if (!familyDoc.IsFamilyDocument) return;

            Application app = familyDoc.Application;
            string oldPath = app.SharedParametersFilename;

            try
            {
                DefinitionFile defFile = GetOrCreateSharedParameterFile(app);
                if (defFile == null) return;

                DefinitionGroup group = defFile.Groups.get_Item("Room Tile parameters");
                if (group == null) return;

                FamilyManager fm = familyDoc.FamilyManager;

                using (Transaction t = new Transaction(familyDoc, "Add Parameters to Family"))
                {
                    t.Start();

                    foreach (Definition def in group.Definitions)
                    {
                        ExternalDefinition extDef = def as ExternalDefinition;
                        if (extDef == null) continue;

                        // 檢查族群是否已存在該參數
                        FamilyParameter existingParam = fm.get_Parameter(def.Name);
                        if (existingParam != null) continue;

                        // 添加共享參數為 Instance Parameter
                        fm.AddParameter(extDef, GroupTypeId.Data, true);
                    }

                    t.Commit();
                }
            }
            finally
            {
                app.SharedParametersFilename = oldPath;
            }
        }

        private static string GetDataTypeString(ForgeTypeId type)
        {
            if (type == SpecTypeId.Length) return "LENGTH";
            if (type == SpecTypeId.Angle) return "ANGLE";
            if (type == SpecTypeId.Int.Integer) return "INTEGER";
            if (type == SpecTypeId.Boolean.YesNo) return "YESNO";
            return "TEXT";
        }
    }
}

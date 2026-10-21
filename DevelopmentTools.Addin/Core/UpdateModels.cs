namespace DevelopmentTools.Core
{
    public class LocalVersionInfo
    {
        public string app_id { get; set; }
        public string product_name { get; set; }
        public string current_version { get; set; }
        public string channel { get; set; }
        public string main_dll { get; set; }
        public string install_folder { get; set; }
        public string updater_path { get; set; }
        public string manifest_url { get; set; }
        public string updated_at { get; set; }
    }

    public class UpdateManifest
    {
        public string app_id { get; set; }
        public string product_name { get; set; }
        public string latest_version { get; set; }
        public string channel { get; set; }
        public string release_url { get; set; }
        public string sha256 { get; set; }
        public bool force_update { get; set; }
        public string minimum_supported_version { get; set; }
        public string release_note { get; set; }
        public string published_at { get; set; }
    }

    public class PendingUpdateInfo
    {
        public string zip_path { get; set; }
        public string sha256 { get; set; }
        public string install_dir { get; set; }
        public string updater_dir { get; set; }
        public string backup_dir { get; set; }
        public string version_file_path { get; set; }
        public string new_version { get; set; }
        public string updated_at { get; set; }
        public int revit_process_id { get; set; }

        // v3.2.14+：避開系統碟（C:）的安裝與暫存位置。舊版 Updater 會忽略這些欄位。
        public string source_install_dir { get; set; }
        public string source_updater_dir { get; set; }
        public string extract_dir { get; set; }
        public string log_dir { get; set; }
        public System.Collections.Generic.List<string> addin_manifest_paths { get; set; }
    }
}

namespace DevelopmentTools.ProjectStandards.Domain.Diff
{
    public class FieldDiff
    {
        public string FieldName { get; set; }

        public string MasterValue { get; set; }

        public string TargetValue { get; set; }

        public DiffStatus Status { get; set; }

        public string Description { get; set; }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case DiffStatus.Equal: return "相同";
                    case DiffStatus.Modified: return "差異 ⚠";
                    case DiffStatus.Missing: return "缺漏 ✕";
                    case DiffStatus.Conflict: return "衝突 🚫";
                    case DiffStatus.DependencyMissing: return "依賴缺漏";
                    default: return Status.ToString();
                }
            }
        }

        public override string ToString()
        {
            return string.Format("{0}: Master=[{1}], Target=[{2}] -> {3}", FieldName, MasterValue, TargetValue, Status);
        }
    }
}

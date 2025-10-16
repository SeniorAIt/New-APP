// File: Models/Workbooks/OrgInfoDataExtensions.cs
namespace WorkbookManagement.Models
{
    public static class OrgInfoDataExtensions
    {
        public static OrgInfoData RecalculateOverview(this OrgInfoData d)
        {
            OrgInfoOverviewBuilder.Build(d);
            return d;
        }
    }
}

// File: Models/Workbooks/OrgInfoOverviewBuilder.cs
using System;
using System.Linq;
using System.Collections.Generic;

namespace WorkbookManagement.Models
{
    public static class OrgInfoOverviewBuilder
    {
        public static void Build(OrgInfoData d)
        {
            if (d == null) return;

            // ---- Top line fields on Section1 --------------------------------
            d.Section1 ??= new OrgInfoSection1();

            // Years registered/accredited (if not already captured)
            if (string.IsNullOrWhiteSpace(d.Section1.YearsRegAccredited) && d.Section1.RegisteredAccreditedSince is DateTime since)
            {
                var years = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - since).TotalDays / 365.25));
                d.Section1.YearsRegAccredited = years.ToString();
            }

            // Board of Directors (string) — prefer an explicit value; else count list
            if (string.IsNullOrWhiteSpace(d.Section1.BoardOfDirectors))
            {
                var total = d.Board?.TotalDirectors
                            ?? d.Board?.Directors?.Count
                            ?? 0;
                if (total > 0) d.Section1.BoardOfDirectors = total.ToString();
            }

            // Campuses / Sites (string) — prefer explicit; else count sites
            if (string.IsNullOrWhiteSpace(d.Section1.CampusesSites))
            {
                var sites = d.Campuses?.Sites?.Count ?? 0;
                if (sites > 0) d.Section1.CampusesSites = sites.ToString();
            }

            // ---- Section 3: Qualifications / Reg Status / Provinces ----------
            d.Section3 ??= new OrgInfoSection3();
            d.Section3.Qualifications ??= new List<QualificationItem>();
            d.Section3.RegistrationStatuses ??= new List<RegistrationStatusItem>();
            d.Section3.ActiveProvinces ??= new List<ProvinceCount>();

            // Qualifications list for overview
            d.Section3.Qualifications.Clear();
            foreach (var q in d.Qualifications?.Items ?? Enumerable.Empty<QualificationCourseRow>())
            {
                if (string.IsNullOrWhiteSpace(q?.Name)) continue;
                d.Section3.Qualifications.Add(new QualificationItem
                {
                    Name = q.Name!,
                    Offered = InferOffered(q),
                    Quantity = null // controller can populate quantity
                });
            }

            // Registration status counts grouped
            d.Section3.RegistrationStatuses.Clear();
            var statusGroups = (d.Qualifications?.Items ?? new List<QualificationCourseRow>())
                .GroupBy(x => string.IsNullOrWhiteSpace(x.RegisteredAccreditedStatus) ? "(Unspecified)" : x.RegisteredAccreditedStatus!.Trim())
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var g in statusGroups)
                d.Section3.RegistrationStatuses.Add(new RegistrationStatusItem { Name = g.Key, Count = g.Count() });

            // Active provinces grouped from campuses
            d.Section3.ActiveProvinces.Clear();
            var provGroups = (d.Campuses?.Sites ?? new List<CampusSiteRow>())
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Province) ? "(Unspecified)" : x.Province!.Trim())
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var g in provGroups)
                d.Section3.ActiveProvinces.Add(new ProvinceCount { Name = g.Key, Count = g.Count() });

            // ---- Section 4: Delivery Modes ----------------------------------
            d.Section4 ??= new OrgInfoSection4();
            DeriveModesFromQualifications(d);

            // ---- Section 5: Student Stats (Historical) -----------------------
            d.Section5 ??= new OrgInfoSection5();
            if ((d.StudentHistorical?.Rows?.Count ?? 0) > 0)
                FillHistoricalTotalsFromRows(d);

            // ---- Section 6: Employee Stats (UPDATED — from Step5 Positions) --
            d.Section6 ??= new OrgInfoSection6();
            d.Section6.EmployeeStats = BuildEmployeeStatsFromPositions(d);

            // ---- Section 7: Student Stats (Current) --------------------------
            d.Section7 ??= new OrgInfoSection7();
            if ((d.StudentCurrent?.Rows?.Count ?? 0) > 0)
                FillCurrentTotalsFromRows(d);
        }

        // --- Helpers (unchanged) --------------------------------------------

        private static bool InferOffered(QualificationCourseRow q)
        {
            var status = (q.RegisteredAccreditedStatus ?? "").Trim().ToLowerInvariant();
            if (status.Contains("current") || status.Contains("accredit") || status.Contains("active"))
                return true;

            return !string.IsNullOrWhiteSpace(q.ModeOfDelivery);
        }

        private static void DeriveModesFromQualifications(OrgInfoData d)
        {
            var has = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var q in d.Qualifications?.Items ?? Enumerable.Empty<QualificationCourseRow>())
            {
                var mode = (q.ModeOfDelivery ?? "").ToLowerInvariant();
                if (mode.Contains("full") && mode.Contains("person")) has.Add("full");
                if (mode.Contains("part") && mode.Contains("person")) has.Add("part");
                if (mode.Contains("distance")) has.Add("distance");
                if (mode.Contains("blend")) has.Add("blended");
                if (mode.Contains("online") || mode.Contains("e-learning") || mode.Contains("elearning")) has.Add("online");
                if (mode.Contains("workplace")) has.Add("workplace");
            }

            d.Section4.FullTimeInPerson |= has.Contains("full");
            d.Section4.PartTimeInPerson |= has.Contains("part");
            d.Section4.DistanceLearning |= has.Contains("distance");
            d.Section4.BlendedLearning |= has.Contains("blended");
            d.Section4.OnlineELearning |= has.Contains("online");
            d.Section4.WorkplaceBased |= has.Contains("workplace");
        }

        private static void FillHistoricalTotalsFromRows(OrgInfoData d)
        {
            var rows = d.StudentHistorical!.Rows!;
            int Sum(Func<StudentHistoricalRow, int?> f) => rows.Sum(r => f(r) ?? 0);

            var enrolled = rows.Any(r => r.Total.HasValue)
                ? Sum(r => r.Total)
                : rows.Sum(r => SafeSum(r.African) + SafeSum(r.Coloured) + SafeSum(r.Indian) + SafeSum(r.White));

            d.Section5.Enrolled = enrolled;
            d.Section5.SuccessfulCompletion = Sum(r => r.SC);
            d.Section5.ResubmissionReassessment = Sum(r => r.PR);
            d.Section5.DropOffsIncomplete = Sum(r => r.DI);

            var male = rows.Sum(r => (r.African.M ?? 0) + (r.Coloured.M ?? 0) + (r.Indian.M ?? 0) + (r.White.M ?? 0));
            var female = rows.Sum(r => (r.African.F ?? 0) + (r.Coloured.F ?? 0) + (r.Indian.F ?? 0) + (r.White.F ?? 0));
            var disabled = rows.Sum(r => (r.African.MD ?? 0) + (r.Coloured.MD ?? 0) + (r.Indian.MD ?? 0) + (r.White.MD ?? 0)
                                         + (r.African.FD ?? 0) + (r.Coloured.FD ?? 0) + (r.Indian.FD ?? 0) + (r.White.FD ?? 0));

            if (male > 0) d.Section5.Male = male;
            if (female > 0) d.Section5.Female = female;
            if (disabled > 0) d.Section5.Disabled = disabled;

            d.Section5.PeriodFrom ??= d.StudentHistorical.PeriodFrom;
            d.Section5.PeriodTo ??= d.StudentHistorical.PeriodTo;
            d.Section5.Months ??= d.StudentHistorical.Months;
        }

        private static void FillCurrentTotalsFromRows(OrgInfoData d)
        {
            var rows = d.StudentCurrent!.Rows!;
            int Sum(Func<StudentCurrentRow, int?> f) => rows.Sum(r => f(r) ?? 0);

            var enrolled = rows.Any(r => r.Total.HasValue)
                ? Sum(r => r.Total)
                : rows.Sum(r => SafeSum(r.African) + SafeSum(r.Coloured) + SafeSum(r.Indian) + SafeSum(r.White));

            d.Section7.Enrolled = enrolled;
            d.Section7.InProcess = Sum(r => r.IP);
            d.Section7.SuccessfulCompletion = Sum(r => r.SC);
            d.Section7.ResubmissionReassessment = Sum(r => r.PR);
            d.Section7.DropOffsIncomplete = Sum(r => r.DI);

            var male = rows.Sum(r => (r.African.M ?? 0) + (r.Coloured.M ?? 0) + (r.Indian.M ?? 0) + (r.White.M ?? 0));
            var female = rows.Sum(r => (r.African.F ?? 0) + (r.Coloured.F ?? 0) + (r.Indian.F ?? 0) + (r.White.F ?? 0));
            var disabled = rows.Sum(r => (r.African.MD ?? 0) + (r.Coloured.MD ?? 0) + (r.Indian.MD ?? 0) + (r.White.MD ?? 0)
                                         + (r.African.FD ?? 0) + (r.Coloured.FD ?? 0) + (r.Indian.FD ?? 0) + (r.White.FD ?? 0));

            if (male > 0) d.Section7.Male = male;
            if (female > 0) d.Section7.Female = female;
            if (disabled > 0) d.Section7.Disabled = disabled;

            if (string.IsNullOrWhiteSpace(d.Section7.PeriodText) && d.StudentCurrent.PeriodFrom.HasValue && d.StudentCurrent.PeriodTo.HasValue)
                d.Section7.PeriodText = $"{d.StudentCurrent.PeriodFrom:yyyy-MM-dd} to {d.StudentCurrent.PeriodTo:yyyy-MM-dd}";
            d.Section7.Months ??= d.StudentCurrent.Months;
        }

        // ---- NEW: Build Employee Stats from Step5 Positions -----------------
        private static List<EmployeeStatRow> BuildEmployeeStatsFromPositions(OrgInfoData d)
        {
            var positions = d.Employment?.Positions ?? new List<EmploymentPosition>();
            if (positions.Count == 0) return new List<EmployeeStatRow>();

            // Sum helpers
            static int SumM(IEnumerable<EmploymentPosition> ps, Func<EmploymentPosition, RacialGenderCounts> pick) => ps.Sum(p => pick(p).M ?? 0);
            static int SumMD(IEnumerable<EmploymentPosition> ps, Func<EmploymentPosition, RacialGenderCounts> pick) => ps.Sum(p => pick(p).MD ?? 0);
            static int SumF(IEnumerable<EmploymentPosition> ps, Func<EmploymentPosition, RacialGenderCounts> pick) => ps.Sum(p => pick(p).F ?? 0);
            static int SumFD(IEnumerable<EmploymentPosition> ps, Func<EmploymentPosition, RacialGenderCounts> pick) => ps.Sum(p => pick(p).FD ?? 0);

            // Race composites
            var afr = (M: SumM(positions, p => p.African), MD: SumMD(positions, p => p.African),
                       F: SumF(positions, p => p.African), FD: SumFD(positions, p => p.African));

            var col = (M: SumM(positions, p => p.Coloured), MD: SumMD(positions, p => p.Coloured),
                       F: SumF(positions, p => p.Coloured), FD: SumFD(positions, p => p.Coloured));

            var ind = (M: SumM(positions, p => p.Indian), MD: SumMD(positions, p => p.Indian),
                       F: SumF(positions, p => p.Indian), FD: SumFD(positions, p => p.Indian));

            var wht = (M: SumM(positions, p => p.White), MD: SumMD(positions, p => p.White),
                       F: SumF(positions, p => p.White), FD: SumFD(positions, p => p.White));

            var total = (
                M: afr.M + col.M + ind.M + wht.M,
                MD: afr.MD + col.MD + ind.MD + wht.MD,
                F: afr.F + col.F + ind.F + wht.F,
                FD: afr.FD + col.FD + ind.FD + wht.FD
            );

            // % helper: 1 decimal, capped to [0,100]
            static decimal? P(int part, int denom)
            {
                if (denom <= 0) return null;
                var v = Math.Round((decimal)part * 100m / denom, 1, MidpointRounding.AwayFromZero);
                if (v > 100m) v = 100m;
                if (v < 0m) v = 0m;
                return v;
            }

            // Row factory — all % are relative to Employ (row total)
            static EmployeeStatRow Row(string label, int m, int md, int f, int fd)
            {
                var employ = m + md + f + fd;
                var disabled = md + fd;

                return new EmployeeStatRow
                {
                    Group = label,
                    Employ = employ,
                    Disabled = disabled,
                    DPercent = P(disabled, employ),

                    Male = m,
                    MalePercent = P(m, employ),

                    MaleDisabled = md,
                    MaleDisabledPercent = P(md, employ),

                    Female = f,
                    FemalePercent = P(f, employ),

                    FemaleDisabled = fd,
                    FemaleDisabledPercent = P(fd, employ),
                };
            }

            return new List<EmployeeStatRow>
            {
                Row("Total",    total.M, total.MD, total.F, total.FD),
                Row("African",  afr.M,   afr.MD,   afr.F,   afr.FD),
                Row("Coloured", col.M,   col.MD,   col.F,   col.FD),
                Row("Indian",   ind.M,   ind.MD,   ind.F,   ind.FD),
                Row("White",    wht.M,   wht.MD,   wht.F,   wht.FD),
            };
        }

        // Legacy helper kept for compatibility (no longer used by Build)
        private static void DeriveEmployeeTotalsRow(OrgInfoData d)
        {
            var s = d.Employment?.Summary;
            if (s == null) return;

            int Safe(RaceTotals rt) => (rt.African ?? 0) + (rt.Coloured ?? 0) + (rt.Indian ?? 0) + (rt.White ?? 0);

            var male = Safe(s.TotalMale);
            var fem = Safe(s.TotalFemale);
            var maleD = Safe(s.MaleDisabled);
            var femD = Safe(s.FemaleDisabled);

            var total = male + fem;
            var disabled = maleD + femD;

            var row = new EmployeeStatRow
            {
                Group = "Total",
                Employ = total,
                Disabled = disabled,
                Male = male,
                Female = fem,
                MaleDisabled = maleD,
                FemaleDisabled = femD,
                DPercent = Pct(disabled, total),
                MalePercent = Pct(male, total),
                FemalePercent = Pct(fem, total),
                MaleDisabledPercent = Pct(maleD, total),
                FemaleDisabledPercent = Pct(femD, total)
            };

            d.Section6.EmployeeStats.Add(row);
        }

        private static int SafeSum(GenderBreakdown g) => (g.M ?? 0) + (g.F ?? 0) + (g.MD ?? 0) + (g.FD ?? 0);
        private static decimal? Pct(int part, int whole) => whole <= 0 ? null : Math.Round((decimal)part * 100m / whole, 1);
    }
}

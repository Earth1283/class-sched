using ClassSchedule.Core;
using Xunit;

namespace ClassSchedule.Core.Tests;

public class DayParserTests
{
    [Theory]
    [InlineData("MWF", "Monday,Wednesday,Friday")]
    [InlineData("TTh", "Tuesday,Thursday")]
    [InlineData("TR", "Tuesday,Thursday")]
    [InlineData("Mon/Wed", "Monday,Wednesday")]
    [InlineData("Tues, Thurs", "Tuesday,Thursday")]
    [InlineData("Tuesday Thursday", "Tuesday,Thursday")]
    [InlineData("Sat", "Saturday")]
    [InlineData("MTWThF", "Monday,Tuesday,Wednesday,Thursday,Friday")]
    public void Parses_common_day_lists(string input, string expected)
    {
        Assert.True(DayParser.TryParse(input, out var days));
        Assert.Equal(expected, string.Join(",", days));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("MXF")]
    public void Rejects_unreadable_input(string input) => Assert.False(DayParser.TryParse(input, out _));
}

public class TimeTextTests
{
    [Theory]
    [InlineData("9:00 AM", 9, 0)]
    [InlineData("9am", 9, 0)]
    [InlineData("12:15 pm", 12, 15)]
    [InlineData("12 AM", 0, 0)]
    [InlineData("13:30", 13, 30)]
    [InlineData("4:50 p.m.", 16, 50)]
    public void Parses_times(string input, int h, int m)
    {
        Assert.True(TimeText.TryParse(input, out var t));
        Assert.Equal(new TimeSpan(h, m, 0), t);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("13pm")]
    [InlineData("9:75")]
    [InlineData("noon")]
    public void Rejects_bad_times(string input) => Assert.False(TimeText.TryParse(input, out _));

    [Fact]
    public void Formats_ranges()
    {
        Assert.Equal("9:00–9:50 AM", TimeText.Range(new TimeSpan(9, 0, 0), new TimeSpan(9, 50, 0)));
        Assert.Equal("11:00 AM–12:15 PM", TimeText.Range(new TimeSpan(11, 0, 0), new TimeSpan(12, 15, 0)));
    }

    [Fact]
    public void Formats_countdowns()
    {
        Assert.Equal("45m", TimeText.Until(TimeSpan.FromMinutes(45)));
        Assert.Equal("2h 8m", TimeText.Until(TimeSpan.FromMinutes(128)));
        Assert.Equal("3h", TimeText.Until(TimeSpan.FromHours(3)));
    }
}

public class OverlapLayoutTests
{
    [Fact]
    public void Separate_items_use_one_lane()
    {
        var slots = OverlapLayout.Assign(new[] { (0.0, 50.0), (60.0, 110.0) });
        Assert.All(slots, s => Assert.Equal(new OverlapLayout.Slot(0, 1), s));
    }

    [Fact]
    public void Overlapping_items_share_lanes()
    {
        var slots = OverlapLayout.Assign(new[] { (0.0, 90.0), (30.0, 60.0), (100.0, 120.0) });
        Assert.Equal(new OverlapLayout.Slot(0, 2), slots[0]);
        Assert.Equal(new OverlapLayout.Slot(1, 2), slots[1]);
        Assert.Equal(new OverlapLayout.Slot(0, 1), slots[2]);
    }

    [Fact]
    public void Lane_is_reused_once_free()
    {
        var slots = OverlapLayout.Assign(new[] { (0.0, 100.0), (10.0, 40.0), (50.0, 90.0) });
        Assert.Equal(1, slots[1].Lane);
        Assert.Equal(1, slots[2].Lane);
        Assert.All(slots, s => Assert.Equal(2, s.Lanes));
    }
}

public class QueryTests
{
    private static readonly DateTime Tuesday = new(2026, 10, 20, 10, 52, 0);
    private static ScheduleData Sample() => SampleData.Create(Tuesday);

    [Fact]
    public void Week_starts_on_monday()
    {
        Assert.Equal(new DateTime(2026, 10, 19), ScheduleQueries.WeekStart(Tuesday));
        Assert.Equal(new DateTime(2026, 10, 19), ScheduleQueries.WeekStart(new DateTime(2026, 10, 25)));
        Assert.Equal(new DateTime(2026, 10, 26), ScheduleQueries.WeekStart(new DateTime(2026, 10, 26)));
    }

    [Fact]
    public void Finds_current_and_next_class()
    {
        var info = ScheduleQueries.Now(Sample(), Tuesday);
        Assert.Equal("MATH 240", info.Current?.Course.Code);
        Assert.Equal("PHYS 110", info.Next?.Course.Code);
        Assert.Equal(Tuesday.Date.AddHours(13), info.NextStart);
    }

    [Fact]
    public void Next_class_rolls_over_to_the_next_day()
    {
        var info = ScheduleQueries.Now(Sample(), new DateTime(2026, 10, 20, 18, 0, 0));
        Assert.Null(info.Current);
        Assert.Equal("CS 201", info.Next?.Course.Code);
        Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0), info.NextStart);
    }

    [Fact]
    public void Agenda_marks_status()
    {
        var agenda = ScheduleQueries.Agenda(Sample(), Tuesday);
        Assert.Equal(new[] { AgendaStatus.Now, AgendaStatus.Upcoming, AgendaStatus.Upcoming }, agenda.Select(a => a.Status));
    }

    [Fact]
    public void Hour_range_widens_for_early_and_late_classes()
    {
        var data = Sample();
        Assert.Equal((8, 17), ScheduleQueries.HourRange(data));
        data.Sessions.Add(new ClassSession { CourseId = data.Courses[0].Id, Days = { DayOfWeek.Monday }, Start = new TimeSpan(7, 30, 0), End = new TimeSpan(18, 15, 0) });
        Assert.Equal((7, 19), ScheduleQueries.HourRange(data));
    }

    [Fact]
    public void Weekend_days_appear_only_when_used()
    {
        var data = Sample();
        Assert.Equal(5, ScheduleQueries.VisibleDays(data).Count);
        data.Sessions[0].Days.Add(DayOfWeek.Saturday);
        Assert.Equal(6, ScheduleQueries.VisibleDays(data).Count);
    }

    [Fact]
    public void Hidden_courses_leave_the_grid_but_not_the_now_card()
    {
        var data = Sample();
        data.Courses.First(c => c.Code == "MATH 240").Visible = false;
        Assert.DoesNotContain(ScheduleQueries.SessionsOn(data, DayOfWeek.Tuesday), p => p.Course.Code == "MATH 240");
        Assert.Equal("MATH 240", ScheduleQueries.Now(data, Tuesday).Current?.Course.Code);
    }

    [Fact]
    public void Upcoming_includes_overdue_and_skips_done_and_far_off()
    {
        var data = Sample();
        data.Tasks[0].Done = true;
        data.Tasks.Add(new TaskItem { CourseId = data.Courses[0].Id, Title = "Late", Due = Tuesday.AddDays(-1) });
        data.Tasks.Add(new TaskItem { CourseId = data.Courses[0].Id, Title = "Far", Due = Tuesday.AddDays(60) });
        var titles = ScheduleQueries.Upcoming(data, Tuesday).Select(t => t.Title).ToList();
        Assert.Equal(new[] { "Late", "Problem set 5", "Essay draft" }, titles);
    }

    [Fact]
    public void Removing_the_last_session_removes_an_unused_course()
    {
        var data = Sample();
        var art = data.Courses.First(c => c.Code == "ART 120");
        data.RemoveSession(data.Sessions.Single(s => s.CourseId == art.Id).Id);
        Assert.DoesNotContain(data.Courses, c => c.Id == art.Id);
    }

    [Fact]
    public void Removing_a_session_keeps_a_course_that_has_tasks()
    {
        var data = Sample();
        var math = data.Courses.First(c => c.Code == "MATH 240");
        data.RemoveSession(data.Sessions.Single(s => s.CourseId == math.Id).Id);
        Assert.Contains(data.Courses, c => c.Id == math.Id);
    }

    [Fact]
    public void Stats_sum_credits_and_hours()
    {
        Assert.Equal("5 courses · 18 credits · 16.4 h / week", ScheduleQueries.Stats(Sample()));
    }
}

public class CsvTests
{
    [Fact]
    public void Imports_rows_and_merges_courses_by_code()
    {
        var data = new ScheduleData();
        var csv = "Code,Name,Days,Start,End,Room,Instructor\r\n" +
                  "CS 201,\"Data Structures, Honors\",MWF,9:00 AM,9:50 AM,Eng 304,Dr. Rivera\r\n" +
                  "CS 201,,Tue,3pm,3:50pm,Eng 118,\r\n";
        var r = CsvImporter.Import(csv, data);
        Assert.Empty(r.Errors);
        Assert.Equal(1, r.CoursesAdded);
        Assert.Equal(2, r.SessionsAdded);
        Assert.Equal("Data Structures, Honors", data.Courses.Single().Name);
        Assert.Equal(new TimeSpan(15, 0, 0), data.Sessions[1].Start);
    }

    [Fact]
    public void Reports_empty_files()
    {
        var r = CsvImporter.Import("", new ScheduleData());
        Assert.Equal("The file is empty.", Assert.Single(r.Errors));
    }

    [Fact]
    public void Reports_missing_columns()
    {
        var r = CsvImporter.Import("Code,Days\nCS 1,MWF\n", new ScheduleData());
        Assert.Contains("start", Assert.Single(r.Errors));
    }

    [Fact]
    public void Reports_bad_rows_with_line_numbers_and_keeps_good_ones()
    {
        var csv = "code,days,start,end\nA 1,MWF,9am,10am\nB 2,xyz,9am,10am\nC 3,Tue,11am,10am\n";
        var data = new ScheduleData();
        var r = CsvImporter.Import(csv, data);
        Assert.Equal(1, r.SessionsAdded);
        Assert.Equal(2, r.Errors.Count);
        Assert.StartsWith("Line 3:", r.Errors[0]);
        Assert.StartsWith("Line 4:", r.Errors[1]);
    }

    [Fact]
    public void Skips_duplicates_on_reimport()
    {
        var csv = "code,days,start,end\nA 1,MWF,9am,10am\n";
        var data = new ScheduleData();
        CsvImporter.Import(csv, data);
        var r = CsvImporter.Import(csv, data);
        Assert.Equal(0, r.SessionsAdded);
        Assert.Equal(1, r.Skipped);
        Assert.Single(data.Sessions);
    }

    [Fact]
    public void Reads_semicolon_files_with_a_bom()
    {
        var data = new ScheduleData();
        var r = CsvImporter.Import("﻿code;days;start;end\nA 1;Mon;9:00;10:00\n", data);
        Assert.Empty(r.Errors);
        Assert.Equal(1, r.SessionsAdded);
    }
}

public class IcsTests
{
    [Fact]
    public void Export_then_import_round_trips_classes_and_tasks()
    {
        var source = SampleData.Create(new DateTime(2026, 10, 20));
        var ics = IcsService.Export(source, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        Assert.Contains("RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;UNTIL=", ics);
        Assert.All(ics.Split("\r\n"), l => Assert.True(l.Length <= 71));

        var target = new ScheduleData();
        var r = IcsService.Import(ics, target);
        Assert.Empty(r.Errors);
        Assert.Equal(source.Sessions.Count, target.Sessions.Count);
        Assert.Equal(source.Courses.Count, target.Courses.Count);
        Assert.Equal(source.Tasks.Count, target.Tasks.Count);

        var cs = target.Sessions.First(s => s.Kind == "Lecture" && target.FindCourse(s.CourseId)!.Code == "CS 201");
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, cs.Days);
        Assert.Equal(new TimeSpan(9, 0, 0), cs.Start);
        Assert.Equal("Engineering Hall 304", cs.Location);
        Assert.Equal("Data Structures", target.FindCourse(cs.CourseId)!.Name);
        Assert.Equal("Dr. Mateo Rivera", target.FindCourse(cs.CourseId)!.Instructor);
        Assert.Contains(target.Tasks, t => t.Kind == TaskKind.Exam && t.Title == "Midterm");
    }

    [Fact]
    public void Imports_a_foreign_calendar_with_folded_lines()
    {
        var ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:BIO 101 - Cell\r\n  Biology\r\nDTSTART;TZID=America/Chicago:20260831T140000\r\n" +
                  "DTEND;TZID=America/Chicago:20260831T152000\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,TH\r\nLOCATION:Room 5\\, North\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var data = new ScheduleData();
        var r = IcsService.Import(ics, data);
        Assert.Empty(r.Errors);
        Assert.Equal("BIO 101", data.Courses.Single().Code);
        Assert.Equal("Cell Biology", data.Courses.Single().Name);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Thursday }, data.Sessions.Single().Days);
        Assert.Equal("Room 5, North", data.Sessions.Single().Location);
    }

    [Fact]
    public void Rejects_files_that_are_not_calendars()
    {
        var r = IcsService.Import("hello", new ScheduleData());
        Assert.Single(r.Errors);
    }

    [Fact]
    public void Skips_all_day_events_with_a_message()
    {
        var ics = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Holiday\nDTSTART;VALUE=DATE:20261127\nEND:VEVENT\nEND:VCALENDAR\n";
        var r = IcsService.Import(ics, new ScheduleData());
        Assert.Contains("all-day", Assert.Single(r.Errors));
    }
}

public class ReminderTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 55, 0);

    [Fact]
    public void Reminds_once_before_a_class()
    {
        var data = SampleData.Create(Now);
        var sent = new HashSet<string>();
        var first = ReminderPlanner.Due(data, Now, sent);
        var r = Assert.Single(first, x => x.Key.StartsWith("class:"));
        Assert.Equal("PHYS 110 lecture starts in 5m", r.Title);
        Assert.DoesNotContain(ReminderPlanner.Due(data, Now.AddMinutes(1), sent), x => x.Key.StartsWith("class:"));
    }

    [Fact]
    public void Stays_quiet_when_disabled_or_too_early()
    {
        var data = SampleData.Create(Now);
        Assert.DoesNotContain(ReminderPlanner.Due(data, Now.AddMinutes(-30), new HashSet<string>()), x => x.Key.StartsWith("class:"));
        data.Settings.RemindersEnabled = false;
        Assert.Empty(ReminderPlanner.Due(data, Now, new HashSet<string>()));
    }

    [Fact]
    public void Reminds_about_work_due_within_a_day()
    {
        var data = SampleData.Create(Now);
        data.Tasks.Clear();
        data.Tasks.Add(new TaskItem { CourseId = data.Courses[0].Id, Title = "Quiz", Kind = TaskKind.Exam, Due = Now.AddHours(5) });
        var r = Assert.Single(ReminderPlanner.Due(data, Now, new HashSet<string>()), x => x.Key.StartsWith("task:"));
        Assert.Equal("Exam: Quiz is due in 5h", r.Title);
    }
}

public class StoreTests
{
    [Fact]
    public void Saves_and_loads_the_same_data()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid());
        try
        {
            var store = new ScheduleStore(Path.Combine(dir, "schedule.json"));
            var data = SampleData.Create(new DateTime(2026, 10, 20));
            store.Save(data);

            var loaded = store.Load(DateTime.Today);
            Assert.Equal(data.Courses.Count, loaded.Courses.Count);
            Assert.Equal(data.Sessions[0].Days, loaded.Sessions[0].Days);
            Assert.Equal(data.Sessions[0].Start, loaded.Sessions[0].Start);
            Assert.Equal(TaskKind.Exam, loaded.Tasks[0].Kind);
            Assert.Null(store.LoadWarning);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Remembers_the_window_placement()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid());
        try
        {
            var store = new ScheduleStore(Path.Combine(dir, "schedule.json"));
            var data = new ScheduleData();
            data.Settings.WindowWidth = 1111;
            data.Settings.WindowHeight = 777;
            data.Settings.WindowLeft = 40;
            data.Settings.WindowMaximized = true;
            store.Save(data);

            var loaded = store.Load(DateTime.Today).Settings;
            Assert.Equal(1111, loaded.WindowWidth);
            Assert.Equal(777, loaded.WindowHeight);
            Assert.Equal(40, loaded.WindowLeft);
            Assert.True(loaded.WindowMaximized);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Sets_aside_a_damaged_file_and_starts_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "schedule.json");
            File.WriteAllText(path, "{ not json");
            var store = new ScheduleStore(path);
            var loaded = store.Load(DateTime.Today);
            Assert.Empty(loaded.Courses);
            Assert.NotNull(store.LoadWarning);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(dir, "*.damaged-*"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void First_launch_starts_empty()
    {
        var store = new ScheduleStore(Path.Combine(Path.GetTempPath(), "cs-missing-" + Guid.NewGuid(), "x.json"));
        var data = store.Load(DateTime.Today);
        Assert.Empty(data.Courses);
        Assert.Empty(data.Sessions);
    }
}

public class BulkAddTests
{
    private static BulkRow One(string line)
    {
        var rows = BulkAdd.Parse(line);
        return Assert.Single(rows);
    }

    private static string Days(BulkRow r) => string.Join(",", r.Days.Select(d => d.ToString()[..3]));

    [Fact]
    public void Reads_a_sentence()
    {
        var r = One("CS 101 Intro to Programming MWF 9:00-9:50 Room 204");
        Assert.Equal(BulkStatus.Ok, r.Status);
        Assert.Equal("CS 101", r.Code);
        Assert.Equal("Intro to Programming", r.Name);
        Assert.Equal("Mon,Wed,Fri", Days(r));
        Assert.Equal(new TimeSpan(9, 0, 0), r.Start);
        Assert.Equal(new TimeSpan(9, 50, 0), r.End);
        Assert.Equal("Room 204", r.Location);
        Assert.Equal("Lecture", r.Kind);
    }

    [Fact]
    public void Reads_commas_with_an_instructor()
    {
        var r = One("MATH 201, TTh, 10:30 AM - 11:45 AM, Hall B, Prof. Lee");
        Assert.Equal("MATH 201", r.Code);
        Assert.Equal("", r.Name);
        Assert.Equal("Tue,Thu", Days(r));
        Assert.Equal(new TimeSpan(10, 30, 0), r.Start);
        Assert.Equal(new TimeSpan(11, 45, 0), r.End);
        Assert.Equal("Hall B", r.Location);
        Assert.Equal("Prof. Lee", r.Instructor);
    }

    [Fact]
    public void Reads_spreadsheet_columns_and_guesses_afternoon()
    {
        var r = One("PHYS 110\tMechanics\tMon/Wed\t1:00\t2:15\tSci 204");
        Assert.Equal("Mechanics", r.Name);
        Assert.Equal("Mon,Wed", Days(r));
        Assert.Equal(new TimeSpan(13, 0, 0), r.Start);
        Assert.Equal(new TimeSpan(14, 15, 0), r.End);
        Assert.Equal("Sci 204", r.Location);
    }

    [Fact]
    public void Takes_the_period_from_the_end_time_and_finds_the_kind()
    {
        var r = One("Chem 110 Lab Wed 2-4:50pm Sci 310");
        Assert.Equal("CHEM 110", r.Code);
        Assert.Equal("Lab", r.Kind);
        Assert.Equal(new TimeSpan(14, 0, 0), r.Start);
        Assert.Equal(new TimeSpan(16, 50, 0), r.End);
        Assert.Equal("Sci 310", r.Location);
    }

    [Theory]
    [InlineData("ENG 101 Writing T/Th 11-12:15", 11, 0, 12, 15)]
    [InlineData("ENG 101 Writing T/Th 11am-12:15", 11, 0, 12, 15)]
    [InlineData("BIO 150 Genetics 13:00-14:30 MWF", 13, 0, 14, 30)]
    [InlineData("ART 120 Drawing MW 7-8:30", 7, 0, 8, 30)]
    [InlineData("HIS 200 Rome Fri 12:30 to 1:45 pm", 12, 30, 13, 45)]
    public void Infers_the_period(string line, int sh, int sm, int eh, int em)
    {
        var r = One(line);
        Assert.Equal(BulkStatus.Ok, r.Status);
        Assert.Equal(new TimeSpan(sh, sm, 0), r.Start);
        Assert.Equal(new TimeSpan(eh, em, 0), r.End);
    }

    [Fact]
    public void Days_after_the_time_and_dashes_work()
    {
        var r = One("CS 201 Data Structures 3:30pm–4:45pm TTh Library 2");
        Assert.Equal("Tue,Thu", Days(r));
        Assert.Equal(new TimeSpan(15, 30, 0), r.Start);
        Assert.Equal("Library 2", r.Location);
    }

    [Fact]
    public void Room_abbreviations_are_not_days()
    {
        var r = One("CS 101 Intro MWF 9:00-9:50 Rm 204");
        Assert.Equal("Mon,Wed,Fri", Days(r));
        Assert.Equal("Rm 204", r.Location);
    }

    [Fact]
    public void A_single_time_assumes_a_length()
    {
        var r = One("CS 101 Intro MWF 9am");
        Assert.Equal(BulkStatus.Assumed, r.Status);
        Assert.Equal(new TimeSpan(9, 40, 0), r.End);
        Assert.True(r.CanAdd);
    }

    [Theory]
    [InlineData("Just some words", "time")]
    [InlineData("CS 101 Intro 9-10", "days")]
    [InlineData("MWF 9-10", "course")]
    public void Explains_what_is_missing(string line, string word)
    {
        var r = One(line);
        Assert.Equal(BulkStatus.Error, r.Status);
        Assert.Contains(word, r.Message);
    }

    [Fact]
    public void Header_rows_switch_to_columns()
    {
        var rows = BulkAdd.Parse("Code,Name,Days,Start,End,Room\nCS 101,Intro,MWF,1:00,2:15,B12\nMATH 1,Algebra,TTh,9:00,10:15,A1");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new TimeSpan(13, 0, 0), rows[0].Start);
        Assert.Equal("B12", rows[0].Location);
        Assert.Equal(2, rows[0].Line);
    }

    [Fact]
    public void Skips_blank_lines_and_comments()
    {
        var rows = BulkAdd.Parse("\n# my fall classes\n\nCS 101 Intro MWF 9-9:50\n// ok\n");
        Assert.Single(rows);
        Assert.Equal(4, rows[0].Line);
    }

    [Fact]
    public void Example_text_is_all_valid()
    {
        var rows = BulkAdd.Parse(BulkAdd.Example);
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(BulkStatus.Ok, r.Status));
    }

    [Fact]
    public void Apply_groups_sessions_under_one_course()
    {
        var data = new ScheduleData();
        var result = BulkAdd.Apply(BulkAdd.Parse("PHYS 110 Mechanics MW 1-2:15pm Sci 310\nPHYS 110 Lab Fri 2-4:50pm Sci 112\nMATH 240 Linear Algebra TTh 10:30-11:45"), data);
        Assert.Equal(2, data.Courses.Count);
        Assert.Equal(3, data.Sessions.Count);
        Assert.Equal(3, result.SessionsAdded);
        Assert.Equal(2, result.CoursesAdded);
        Assert.NotEqual(data.Courses[0].Color, data.Courses[1].Color);
    }

    [Fact]
    public void Marks_duplicates_in_the_text_and_in_the_schedule()
    {
        var data = new ScheduleData();
        BulkAdd.Apply(BulkAdd.Parse("CS 101 Intro MWF 9-9:50"), data);

        var rows = BulkAdd.Parse("CS 101 Intro MWF 9-9:50\nMATH 1 Algebra TTh 9-10\nMATH 1 Algebra TTh 9-10", data);
        Assert.Equal(BulkStatus.Duplicate, rows[0].Status);
        Assert.Equal(BulkStatus.Ok, rows[1].Status);
        Assert.Equal(BulkStatus.Duplicate, rows[2].Status);

        var result = BulkAdd.Apply(rows, data);
        Assert.Equal(1, result.SessionsAdded);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(2, data.Sessions.Count);
    }

    [Fact]
    public void Errors_are_left_out_when_applying()
    {
        var data = new ScheduleData();
        var result = BulkAdd.Apply(BulkAdd.Parse("CS 101 Intro MWF 9-9:50\nnonsense"), data);
        Assert.Equal(1, result.SessionsAdded);
    }
}

public class StorageLocationTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid());

    [Fact]
    public void Saves_into_a_folder_that_does_not_exist_yet()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "term", "fall.json");
            new ScheduleStore(path).Save(SampleData.Create(new DateTime(2026, 10, 20)));
            Assert.True(File.Exists(path));
            Assert.NotEmpty(new ScheduleStore(path).Load(DateTime.Today).Sessions);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Config_remembers_a_custom_location()
    {
        var dir = NewDir();
        try
        {
            var file = Path.Combine(dir, "config.json");
            var config = new AppConfig();
            config.Use(Path.Combine(dir, "mine.json"));
            config.Save(file);

            Assert.Equal(Path.Combine(dir, "mine.json"), AppConfig.Load(file).DataPath);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Choosing_the_default_path_clears_the_setting()
    {
        var config = new AppConfig { DataPath = "X:\\somewhere\\schedule.json" };
        config.Use(ScheduleStore.DefaultPath);
        Assert.Null(config.DataPath);
    }

    [Fact]
    public void Startup_uses_the_chosen_file_when_its_folder_exists()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        try
        {
            var config = new AppConfig { DataPath = Path.Combine(dir, "schedule.json") };
            Assert.Equal(config.DataPath, config.ResolveDataPath(out var warning));
            Assert.Null(warning);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Startup_falls_back_to_the_default_when_the_folder_is_gone()
    {
        var config = new AppConfig { DataPath = Path.Combine(NewDir(), "schedule.json") };
        Assert.Equal(ScheduleStore.DefaultPath, config.ResolveDataPath(out var warning));
        Assert.Contains("isn't available", warning);
        Assert.Equal(ScheduleStore.DefaultPath, new AppConfig().ResolveDataPath(out var none));
        Assert.Null(none);
    }

    [Fact]
    public void Opening_reads_a_saved_schedule()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "a.json");
            new ScheduleStore(path).Save(SampleData.Create(new DateTime(2026, 10, 20)));
            var read = ScheduleStore.TryRead(path, out var problem);
            Assert.NotNull(read);
            Assert.Null(problem);
            Assert.NotEmpty(read!.Courses);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("{ \"name\": \"package\", \"version\": \"1.0.0\" }")]
    [InlineData("[1, 2, 3]")]
    [InlineData("not json at all")]
    public void Opening_refuses_other_files_and_leaves_them_alone(string content)
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "other.json");
            File.WriteAllText(path, content);

            Assert.Null(ScheduleStore.TryRead(path, out var problem));
            Assert.False(string.IsNullOrEmpty(problem));
            Assert.Equal(content, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Replacing_contents_keeps_the_same_object()
    {
        var target = new ScheduleData();
        var held = target.Sessions;
        target.ReplaceContentsWith(SampleData.Create(new DateTime(2026, 10, 20)));
        Assert.Same(held, target.Sessions);
        Assert.NotEmpty(target.Sessions);
    }
}

public class WidgetTests
{
    [Fact]
    public void Widget_is_off_by_default_and_sits_behind_other_windows()
    {
        var s = new Settings();
        Assert.False(s.WidgetEnabled);
        Assert.True(s.WidgetBehind);
        Assert.False(s.WidgetCompact);
        Assert.InRange(s.WidgetOpacity, 55, 100);
        Assert.Null(s.WidgetLeft);
    }

    [Fact]
    public void Widget_choices_survive_a_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid());
        try
        {
            var store = new ScheduleStore(Path.Combine(dir, "schedule.json"));
            var data = new ScheduleData();
            data.Settings.WidgetEnabled = true;
            data.Settings.WidgetBehind = false;
            data.Settings.WidgetCompact = true;
            data.Settings.WidgetOpacity = 75;
            data.Settings.WidgetLeft = 1500.5;
            data.Settings.WidgetTop = 24;
            store.Save(data);

            var loaded = store.Load(DateTime.Today).Settings;
            Assert.True(loaded.WidgetEnabled);
            Assert.False(loaded.WidgetBehind);
            Assert.True(loaded.WidgetCompact);
            Assert.Equal(75, loaded.WidgetOpacity);
            Assert.Equal(1500.5, loaded.WidgetLeft);
            Assert.Equal(24, loaded.WidgetTop);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Opening_another_file_keeps_this_pcs_widget_choices()
    {
        var mine = new Settings { WidgetEnabled = true, WidgetOpacity = 60, WidgetLeft = 10 };
        var theirs = new Settings { TermName = "Spring" };
        theirs.KeepPreferencesFrom(mine);
        Assert.Equal("Spring", theirs.TermName);
        Assert.True(theirs.WidgetEnabled);
        Assert.Equal(60, theirs.WidgetOpacity);
        Assert.Equal(10, theirs.WidgetLeft);
    }

    [Fact]
    public void Next_due_skips_done_work_and_todays_work()
    {
        var now = new DateTime(2026, 10, 20, 10, 0, 0);
        var data = new ScheduleData();
        data.Tasks.Add(new TaskItem { Title = "today", Due = new DateTime(2026, 10, 20, 23, 0, 0) });
        data.Tasks.Add(new TaskItem { Title = "finished", Due = new DateTime(2026, 10, 21, 9, 0, 0), Done = true });
        data.Tasks.Add(new TaskItem { Title = "later", Due = new DateTime(2026, 10, 25, 9, 0, 0) });
        data.Tasks.Add(new TaskItem { Title = "sooner", Due = new DateTime(2026, 10, 22, 9, 0, 0) });

        Assert.Equal("sooner", ScheduleQueries.NextDue(data, now)?.Title);
        Assert.Null(ScheduleQueries.NextDue(new ScheduleData(), now));
    }
}

public class UndoTests
{
    private static ScheduleData TwoClasses(out ClassSession first, out ClassSession second)
    {
        var data = new ScheduleData();
        var a = new Course { Code = "A 1" };
        var b = new Course { Code = "B 2" };
        data.Courses.Add(a);
        data.Courses.Add(b);
        first = new ClassSession { CourseId = a.Id, Days = { DayOfWeek.Monday }, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(10) };
        second = new ClassSession { CourseId = b.Id, Days = { DayOfWeek.Tuesday }, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(10) };
        data.Sessions.Add(first);
        data.Sessions.Add(second);
        return data;
    }

    [Fact]
    public void Undo_puts_a_deleted_class_and_its_course_back_in_place()
    {
        var data = TwoClasses(out var first, out _);
        var undo = data.RemoveSessionWithUndo(first.Id);

        Assert.NotNull(undo);
        Assert.Single(data.Sessions);
        Assert.Single(data.Courses);

        undo!();
        Assert.Equal(2, data.Sessions.Count);
        Assert.Equal(first.Id, data.Sessions[0].Id);
        Assert.Equal("A 1", data.Courses[0].Code);
    }

    [Fact]
    public void Undo_twice_does_not_duplicate()
    {
        var data = TwoClasses(out var first, out _);
        var undo = data.RemoveSessionWithUndo(first.Id)!;
        undo();
        undo();
        Assert.Equal(2, data.Sessions.Count);
        Assert.Equal(2, data.Courses.Count);
    }

    [Fact]
    public void Undo_keeps_a_course_that_other_classes_still_use()
    {
        var data = TwoClasses(out var first, out _);
        data.Sessions.Add(new ClassSession { CourseId = first.CourseId, Days = { DayOfWeek.Friday }, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(10) });
        var undo = data.RemoveSessionWithUndo(first.Id)!;
        Assert.Equal(2, data.Courses.Count);
        undo();
        Assert.Equal(2, data.Courses.Count);
        Assert.Equal(3, data.Sessions.Count);
    }

    [Fact]
    public void Unknown_class_has_nothing_to_undo()
    {
        Assert.Null(new ScheduleData().RemoveSessionWithUndo(Guid.NewGuid()));
    }

    [Fact]
    public void Animations_default_on_and_follow_the_pc_between_files()
    {
        Assert.True(new Settings().AnimationsEnabled);
        var mine = new Settings { AnimationsEnabled = false };
        var theirs = new Settings();
        theirs.KeepPreferencesFrom(mine);
        Assert.False(theirs.AnimationsEnabled);
    }
}

public class DeleteOneDayTests
{
    [Fact]
    public void Deleting_one_day_keeps_the_other_days_and_can_be_undone()
    {
        var data = new ScheduleData();
        var course = new Course { Code = "M 1" };
        data.Courses.Add(course);
        var session = new ClassSession { CourseId = course.Id, Days = { DayOfWeek.Tuesday, DayOfWeek.Thursday }, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(10) };
        data.Sessions.Add(session);

        var undo = data.RemoveDayWithUndo(session.Id, DayOfWeek.Tuesday);
        Assert.NotNull(undo);
        Assert.Equal(new[] { DayOfWeek.Thursday }, session.Days);
        Assert.Single(data.Sessions);

        undo!();
        Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday }, session.Days);
    }

    [Fact]
    public void Deleting_the_only_day_removes_the_class()
    {
        var data = new ScheduleData();
        var course = new Course { Code = "M 1" };
        data.Courses.Add(course);
        var session = new ClassSession { CourseId = course.Id, Days = { DayOfWeek.Friday }, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(10) };
        data.Sessions.Add(session);

        var undo = data.RemoveDayWithUndo(session.Id, DayOfWeek.Friday);
        Assert.Empty(data.Sessions);
        undo!();
        Assert.Single(data.Sessions);
    }

    [Fact]
    public void A_day_the_class_does_not_meet_on_has_nothing_to_delete()
    {
        var data = new ScheduleData();
        var session = new ClassSession { Days = { DayOfWeek.Monday } };
        data.Sessions.Add(session);
        Assert.Null(data.RemoveDayWithUndo(session.Id, DayOfWeek.Sunday));
    }
}

public class WeeklyPatternTests
{
    private static string Show(WeeklyPatternResult r) =>
        string.Join("; ", r.Groups().Select(g => $"{string.Join("", g.Days.Select(d => d.ToString()[..2]))} {Clock(g.Start)}-{Clock(g.End)}"));

    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}";

    [Fact]
    public void Each_day_can_have_its_own_times()
    {
        var r = WeeklyPattern.Parse("MON 9:00-9:50, WED 10:00-10:50");
        Assert.True(r.Ok);
        Assert.Equal("Mo 9:00-9:50; We 10:00-10:50", Show(r));
    }

    [Fact]
    public void Times_can_be_listed_as_plain_pairs()
    {
        Assert.Equal("Mo 9:00-9:50; We 10:00-10:50", Show(WeeklyPattern.Parse("MON 9:00, 9:50, WED 10:00, 10:50")));
        Assert.Equal("Mo 9:00-9:50; We 10:00-10:50", Show(WeeklyPattern.Parse("Monday 9:00 AM to 9:50 AM\nWednesday 10:00 AM to 10:50 AM")));
    }

    [Fact]
    public void Days_with_the_same_times_become_one_class()
    {
        Assert.Equal("MoWeFr 9:00-9:50", Show(WeeklyPattern.Parse("Mon 9-9:50, Wed 9-9:50, Fri 9-9:50")));
        Assert.Equal("MoWe 9:00-9:50", Show(WeeklyPattern.Parse("Mon/Wed 9-9:50")));
        Assert.Equal("TuTh 10:30-11:45", Show(WeeklyPattern.Parse("Tue, Thu 10:30-11:45")));
    }

    [Fact]
    public void Day_codes_work_for_several_groups()
    {
        Assert.Equal("MoWeFr 9:00-9:50; TuTh 11:00-12:15", Show(WeeklyPattern.Parse("MWF 9-9:50, TTh 11-12:15")));
    }

    [Fact]
    public void A_day_can_have_two_slots()
    {
        var r = WeeklyPattern.Parse("Mon 9-9:50 2-3:15");
        Assert.True(r.Ok);
        Assert.Equal("Mo 9:00-9:50; Mo 14:00-15:15", Show(r));
    }

    [Fact]
    public void Afternoon_hours_are_guessed_like_bulk_add()
    {
        Assert.Equal("Mo 13:00-14:15", Show(WeeklyPattern.Parse("Mon 1-2:15")));
        Assert.Equal("Mo 13:00-14:15", Show(WeeklyPattern.Parse("Mon 1:00pm-2:15pm")));
        Assert.Equal("Mo 8:00-13:00", Show(WeeklyPattern.Parse("Mon 8am-1pm")));
        Assert.Equal("Fr 18:30-21:00", Show(WeeklyPattern.Parse("Fri 18:30-21:00")));
    }

    [Fact]
    public void Repeats_are_counted_once()
    {
        Assert.Single(WeeklyPattern.Parse("Mon 9-10, Mon 9-10").Slots);
    }

    [Fact]
    public void Empty_text_is_neither_ok_nor_an_error()
    {
        var r = WeeklyPattern.Parse("   ");
        Assert.True(r.IsEmpty);
        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData("9:00-9:50", "day")]
    [InlineData("Mon", "start and an end")]
    [InlineData("Mon 9:00", "partner")]
    [InlineData("Mon 10-9", "valid time range")]
    [InlineData("Funday 9-10", "isn't a day")]
    [InlineData("Mon 25:00-26:00", "valid time range")]
    public void Problems_are_explained(string text, string fragment)
    {
        var r = WeeklyPattern.Parse(text);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains(fragment));
    }

    [Fact]
    public void One_bad_part_does_not_hide_the_good_ones()
    {
        var r = WeeklyPattern.Parse("Mon 9-9:50, Tue 10, Wed 11-11:50");
        Assert.False(r.Ok);
        Assert.Equal(2, r.Slots.Count);
        Assert.Single(r.Errors);
    }

    [Fact]
    public void Written_text_reads_back_the_same()
    {
        var text = WeeklyPattern.Write(new[] { DayOfWeek.Friday, DayOfWeek.Monday }, TimeSpan.FromHours(13), TimeSpan.FromHours(14.25));
        Assert.Equal("Mon Fri 1:00 PM - 2:15 PM", text);
        Assert.Equal("MoFr 13:00-14:15", Show(WeeklyPattern.Parse(text)));
    }
}

public class ClassDefaultsTests
{
    [Fact]
    public void A_new_class_is_forty_minutes_long()
    {
        Assert.Equal(40, ClassDefaults.Minutes);
        Assert.Equal(new TimeSpan(9, 40, 0), ClassDefaults.Start + ClassDefaults.Length);
    }

    [Fact]
    public void Bulk_add_gives_a_lone_start_time_the_default_length()
    {
        var row = BulkAdd.Parse("CS 101 Intro MWF 1pm").Single();
        Assert.Equal(new TimeSpan(13, 0, 0), row.Start);
        Assert.Equal(new TimeSpan(13, 40, 0), row.End);
    }
}

public class PaletteTests
{
    [Fact]
    public void The_palette_has_many_distinct_colors()
    {
        Assert.True(CoursePalette.Keys.Length >= 16);
        Assert.Equal(CoursePalette.Keys.Length, CoursePalette.Keys.Distinct().Count());
    }

    [Fact]
    public void New_courses_cycle_through_every_color_before_repeating()
    {
        var first = Enumerable.Range(0, CoursePalette.Keys.Length).Select(CoursePalette.ForIndex).ToList();
        Assert.Equal(CoursePalette.Keys.Length, first.Distinct().Count());
        Assert.Equal(CoursePalette.Keys[0], CoursePalette.ForIndex(CoursePalette.Keys.Length));
    }

    [Fact]
    public void Existing_schedules_keep_their_colors()
    {
        Assert.Equal(new[] { "Blue", "Green", "Orange", "Violet", "Rose", "Teal" }, CoursePalette.Keys.Take(6));
    }
}

namespace ClassSchedule.Core;

/// <summary>Example schedule the user can load from the empty-state screen.</summary>
public static class SampleData
{
    public static ScheduleData Create(DateTime today)
    {
        var data = new ScheduleData();
        data.Settings.TermName = "Sample term";
        data.Settings.TermStart = ScheduleQueries.WeekStart(today).AddDays(-7 * 8);
        data.Settings.TermEnd = data.Settings.TermStart.AddDays(7 * 15 - 3);
        var reading = data.Settings.TermStart.AddDays(7 * 11);
        data.Settings.Breaks.Add(new TermBreak { Name = "Fall break", Start = reading, End = reading.AddDays(4) });

        Course Add(string code, string name, string who, int credits, string color)
        {
            var c = new Course { Code = code, Name = name, Instructor = who, Credits = credits, Color = color };
            data.Courses.Add(c);
            return c;
        }

        void Session(Course c, string kind, string days, string start, string end, string room)
        {
            DayParser.TryParse(days, out var ds);
            TimeText.TryParse(start, out var s);
            TimeText.TryParse(end, out var e);
            data.Sessions.Add(new ClassSession { CourseId = c.Id, Kind = kind, Days = ds, Start = s, End = e, Location = room });
        }

        var cs = Add("CS 201", "Data Structures", "Dr. Mateo Rivera", 4, "Blue");
        var math = Add("MATH 240", "Linear Algebra", "Prof. Ada Okafor", 4, "Green");
        var phys = Add("PHYS 110", "Mechanics", "Dr. Hana Lindgren", 4, "Orange");
        var eng = Add("ENG 105", "Writing Seminar", "Dr. Elias Lindqvist", 3, "Violet");
        var art = Add("ART 120", "Drawing Studio", "Prof. Noor Haddad", 3, "Rose");

        Session(cs, "Lecture", "MWF", "9:00 AM", "9:50 AM", "Engineering Hall 304");
        Session(cs, "Recitation", "Tue", "3:00 PM", "3:50 PM", "Engineering Hall 118");
        Session(math, "Lecture", "TTh", "10:30 AM", "11:45 AM", "Hall B 112");
        Session(eng, "Seminar", "MW", "11:00 AM", "12:15 PM", "Humanities 220");
        Session(phys, "Lecture", "TTh", "1:00 PM", "2:15 PM", "Science 12");
        Session(phys, "Lab", "Wed", "2:00 PM", "4:50 PM", "Science Lab 3");
        Session(art, "Studio", "Fri", "1:00 PM", "3:45 PM", "Fine Arts, Studio B");

        data.Tasks.Add(new TaskItem { CourseId = math.Id, Title = "Midterm", Kind = TaskKind.Exam, Due = today.Date.AddDays(3).AddHours(10).AddMinutes(30) });
        data.Tasks.Add(new TaskItem { CourseId = cs.Id, Title = "Problem set 5", Kind = TaskKind.Assignment, Due = today.Date.AddDays(5).AddHours(23).AddMinutes(59) });
        data.Tasks.Add(new TaskItem { CourseId = eng.Id, Title = "Essay draft", Kind = TaskKind.Assignment, Due = today.Date.AddDays(8).AddHours(17) });
        return data;
    }
}

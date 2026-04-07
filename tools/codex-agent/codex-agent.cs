using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_codex_agent.main();

public static class Codex_codex_agent
{
    public static long parse_int_or(string s, long default_val)
    {
        return ((((long)s.Length) == 0L) ? default_val : long.Parse(s));
    }

    public static string pad_right(string s, long width)
    {
        while (true)
        {
            if ((((long)s.Length) >= width))
            {
                return s;
            }
            else
            {
                var _tco_0 = string.Concat(s, " ");
                var _tco_1 = width;
                s = _tco_0;
                width = _tco_1;
                continue;
            }
        }
    }

    public static string pad_left(string s, long width)
    {
        while (true)
        {
            if ((((long)s.Length) >= width))
            {
                return s;
            }
            else
            {
                var _tco_0 = string.Concat(" ", s);
                var _tco_1 = width;
                s = _tco_0;
                width = _tco_1;
                continue;
            }
        }
    }

    public static long count_lines(string content)
    {
        return ((((long)content.Length) == 0L) ? 0L : ((long)new List<string>(content.Split("\n")).Count));
    }

    public static string snap_path(string file)
    {
        return string.Concat(file, ".snap");
    }

    public static string size_hint(long lines)
    {
        return ((lines > 300L) ? " !! LARGE" : ((lines > 100L) ? " ~  medium" : ""));
    }

    public static string format_line(long num, string line)
    {
        return string.Concat(pad_left((num).ToString(), 5L), string.Concat("  ", line));
    }

    public static string format_lines_loop(List<string> lines, long current, long end_line)
    {
        return ((current > end_line) ? "" : ((current > ((long)lines.Count)) ? "" : ((Func<string, string>)((line) => ((Func<string, string>)((formatted) => ((Func<string, string>)((rest) => ((((long)rest.Length) == 0L) ? formatted : string.Concat(formatted, string.Concat("\n", rest)))))(format_lines_loop(lines, (current + 1L), end_line))))(format_line(current, line))))(lines[(int)(current - 1L)])));
    }

    public static object do_peek(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? Console.WriteLine("Usage: codex-agent peek <file> [start] [end]") : ((Func<string, object>)((file) => (File.Exists(file) ? ((Func<string, object>)((content) => ((Func<List<string>, object>)((all_lines) => ((Func<long, object>)((total) => ((Func<long, object>)((raw_start) => ((Func<long, object>)((start) => ((Func<long, object>)((end_line) => ((Func<long, object>)((actual_end) => ((Func<string, object>)((header) => Console.WriteLine(string.Concat(header, string.Concat("\n", format_lines_loop(all_lines, start, actual_end))))))(string.Concat("--- ", string.Concat(file, string.Concat(" lines ", string.Concat((start).ToString(), string.Concat("..", string.Concat((actual_end).ToString(), string.Concat(" of ", string.Concat((total).ToString(), " ---")))))))))))(((end_line == 0L) ? total : end_line))))(((argc > 4L) ? parse_int_or(args[(int)4L], 0L) : 0L))))(((raw_start < 1L) ? 1L : raw_start))))(((argc > 3L) ? parse_int_or(args[(int)3L], 1L) : 1L))))(((long)all_lines.Count))))(new List<string>(content.Split("\n")))))(File.ReadAllText(file)) : Console.WriteLine(string.Concat("File not found: ", file)))))(args[(int)2L]))))(((long)args.Count));
        return null;
    }

    public static string stat_loop(List<string> args, long idx, long total_lines, long total_chars)
    {
        return ((idx >= ((long)args.Count)) ? ((idx > 3L) ? string.Concat("\n", string.Concat(pad_right(string.Concat("TOTAL (", string.Concat(((idx - 2L)).ToString(), " files)")), 50L), string.Concat(pad_left((total_lines).ToString(), 7L), pad_left((total_chars).ToString(), 9L)))) : "") : ((Func<string, string>)((file) => (File.Exists(file) ? ((Func<string, string>)((content) => ((Func<long, string>)((lines) => ((Func<long, string>)((chars) => ((Func<string, string>)((row) => ((Func<string, string>)((rest) => string.Concat(row, string.Concat("\n", rest))))(stat_loop(args, (idx + 1L), (total_lines + lines), (total_chars + chars)))))(string.Concat(pad_right(file, 50L), string.Concat(pad_left((lines).ToString(), 7L), string.Concat(pad_left((chars).ToString(), 9L), size_hint(lines)))))))(((long)content.Length))))(count_lines(content))))(File.ReadAllText(file)) : ((Func<string, string>)((rest) => string.Concat("  (not found: ", string.Concat(file, string.Concat(")\n", rest)))))(stat_loop(args, (idx + 1L), total_lines, total_chars)))))(args[(int)idx]));
    }

    public static object do_stat(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? Console.WriteLine("Usage: codex-agent stat <file> [file2] ...") : ((Func<string, object>)((header) => ((Func<string, object>)((sep) => Console.WriteLine(string.Concat(header, string.Concat("\n", string.Concat(sep, string.Concat("\n", stat_loop(args, 2L, 0L, 0L))))))))("-------------------------------------------------- ------- --------")))(string.Concat(pad_right("File", 50L), string.Concat(pad_left("Lines", 7L), pad_left("Chars", 9L)))))))(((long)args.Count));
        return null;
    }

    public static object do_snap_save(string file)
    {
        (File.Exists(file) ? ((Func<string, object>)((content) => ((Func<long, object>)((lines) => ((Func<object, object>)((_) => Console.WriteLine(string.Concat("  Snapshot saved: ", string.Concat(file, string.Concat(" (", string.Concat((lines).ToString(), " lines)")))))))(File.WriteAllText(snap_path(file), content))))(count_lines(content))))(File.ReadAllText(file)) : Console.WriteLine(string.Concat("  File not found: ", file)));
        return null;
    }

    public static object do_snap_restore(string file)
    {
        ((Func<string, object>)((snap) => (File.Exists(snap) ? ((Func<string, object>)((content) => ((Func<long, object>)((lines) => ((Func<object, object>)((_) => Console.WriteLine(string.Concat("  Restored: ", string.Concat(file, string.Concat(" (", string.Concat((lines).ToString(), " lines)")))))))(File.WriteAllText(file, content))))(count_lines(content))))(File.ReadAllText(snap)) : Console.WriteLine(string.Concat("  No snapshot found for ", string.Concat(file, ". Run 'snap save' first."))))))(snap_path(file));
        return null;
    }

    public static object do_snap_diff(string file)
    {
        ((Func<string, object>)((snap) => (File.Exists(snap) ? (File.Exists(file) ? ((Func<string, object>)((old_content) => ((Func<string, object>)((new_content) => ((Func<long, object>)((old_lines) => ((Func<long, object>)((new_lines) => ((Func<long, object>)((delta) => ((old_content == new_content) ? Console.WriteLine(string.Concat("-- ", string.Concat(file, string.Concat(": no changes (", string.Concat((old_lines).ToString(), " lines) --"))))) : Console.WriteLine(string.Concat("-- ", string.Concat(file, string.Concat(" --\n  Before: ", string.Concat((old_lines).ToString(), string.Concat(" lines   After: ", string.Concat((new_lines).ToString(), string.Concat(" lines   Delta: ", string.Concat((delta).ToString(), string.Concat("\n  Use 'codex-agent peek ", string.Concat(file, "' to inspect.\n-- End diff --"))))))))))))))((new_lines - old_lines))))(count_lines(new_content))))(count_lines(old_content))))(File.ReadAllText(file))))(File.ReadAllText(snap)) : Console.WriteLine(string.Concat("  File not found: ", file))) : Console.WriteLine(string.Concat("  No snapshot for ", string.Concat(file, string.Concat(". Run 'codex-agent snap save ", string.Concat(file, "' first."))))))))(snap_path(file));
        return null;
    }

    public static object do_snap(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? Console.WriteLine("Usage: codex-agent snap <save|diff|restore> <file>") : ((Func<string, object>)((action) => ((action == "save") ? ((argc < 4L) ? Console.WriteLine("Usage: codex-agent snap save <file>") : do_snap_save(args[(int)3L])) : ((action == "diff") ? ((argc < 4L) ? Console.WriteLine("Usage: codex-agent snap diff <file>") : do_snap_diff(args[(int)3L])) : ((action == "restore") ? ((argc < 4L) ? Console.WriteLine("Usage: codex-agent snap restore <file>") : do_snap_restore(args[(int)3L])) : Console.WriteLine(string.Concat("Unknown snap action: ", string.Concat(action, ". Use save, diff, or restore."))))))))(args[(int)2L]))))(((long)args.Count));
        return null;
    }

    public static string check_file_status(string label, string path)
    {
        return (File.Exists(path) ? string.Concat(label, ": found") : string.Concat(label, ": MISSING"));
    }

    public static object do_status(List<string> args)
    {
        Console.WriteLine(string.Concat("=== Codex Agent Status ===\n", string.Concat(check_file_status("  Solution     ", "Codex.sln"), string.Concat("\n", string.Concat(check_file_status("  Build props  ", "Directory.Build.props"), string.Concat("\n", string.Concat(check_file_status("  Contributing ", "CONTRIBUTING.md"), string.Concat("\n", string.Concat(check_file_status("  Lock doc     ", "docs/Compiler/REFERENCE-COMPILER-LOCK.md"), string.Concat("\n", string.Concat(check_file_status("  Current plan ", "docs/CurrentPlan.md"), string.Concat("\n", string.Concat(check_file_status("  Agent rules  ", ".github/copilot-instructions.md"), string.Concat("\n", "=========================="))))))))))))));
        return null;
    }

    public static string plan_file()
    {
        return ".codex-agent/plan.txt";
    }

    public static string log_file()
    {
        return ".codex-agent/session.log";
    }

    public static string build_log()
    {
        return ".codex-agent/last-build.txt";
    }

    public static string test_log()
    {
        return ".codex-agent/last-test.txt";
    }

    public static string known_conditions_file()
    {
        return "docs/KNOWN-CONDITIONS.md";
    }

    public static string handoff_file()
    {
        return ".handoff";
    }

    public static string collect_args(List<string> args, long idx, long start)
    {
        return ((idx >= ((long)args.Count)) ? "" : ((Func<string, string>)((word) => ((idx == start) ? string.Concat(word, collect_args(args, (idx + 1L), start)) : string.Concat(" ", string.Concat(word, collect_args(args, (idx + 1L), start))))))(args[(int)idx]));
    }

    public static string kv_get(string key, List<string> lines, long idx)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return "";
            }
            else
            {
                var line = lines[(int)idx];
                var prefix = string.Concat(key, "=");
                if (line.StartsWith(prefix))
                {
                    return line.Substring((int)((long)prefix.Length), (int)(((long)line.Length) - ((long)prefix.Length)));
                }
                else
                {
                    var _tco_0 = key;
                    var _tco_1 = lines;
                    var _tco_2 = (idx + 1L);
                    key = _tco_0;
                    lines = _tco_1;
                    idx = _tco_2;
                    continue;
                }
            }
        }
    }

    public static string kv_set_loop(string key, string value, List<string> lines, string prefix, long idx, long found)
    {
        return ((idx >= ((long)lines.Count)) ? ((found == 0L) ? string.Concat(key, string.Concat("=", string.Concat(value, "\n"))) : "") : ((Func<string, string>)((line) => ((Func<string, string>)((rest) => (line.StartsWith(prefix) ? string.Concat(key, string.Concat("=", string.Concat(value, string.Concat("\n", rest)))) : ((((long)line.Length) == 0L) ? rest : string.Concat(line, string.Concat("\n", rest))))))(kv_set_loop(key, value, lines, prefix, (idx + 1L), (line.StartsWith(prefix) ? 1L : found)))))(lines[(int)idx]));
    }

    public static string kv_set(string key, string value, string content)
    {
        return ((Func<List<string>, string>)((lines) => ((Func<string, string>)((prefix) => kv_set_loop(key, value, lines, prefix, 0L, 0L)))(string.Concat(key, "="))))(new List<string>(content.Split("\n")));
    }

    public static long valid_transition(string from_state, string to_state)
    {
        return ((from_state == "draft") ? ((to_state == "awaiting-review") ? 1L : 0L) : ((from_state == "awaiting-review") ? ((to_state == "under-review") ? 1L : ((to_state == "abandoned") ? 1L : 0L)) : ((from_state == "under-review") ? ((to_state == "approved") ? 1L : ((to_state == "changes-requested") ? 1L : ((to_state == "abandoned") ? 1L : 0L))) : ((from_state == "changes-requested") ? ((to_state == "awaiting-review") ? 1L : ((to_state == "abandoned") ? 1L : 0L)) : ((from_state == "approved") ? ((to_state == "merged") ? 1L : ((to_state == "abandoned") ? 1L : 0L)) : ((from_state == "abandoned") ? ((to_state == "awaiting-review") ? 1L : 0L) : ((from_state == "merged") ? ((to_state == "awaiting-review") ? 1L : 0L) : 0L)))))));
    }

    public static string next_action_hint(string state)
    {
        return ((state == "draft") ? "Author: run 'handoff push <summary>' to request review" : ((state == "awaiting-review") ? "Reviewer: run 'handoff review' to begin review" : ((state == "under-review") ? "Reviewer: run 'handoff approve' or 'handoff request-changes <reason>'" : ((state == "changes-requested") ? "Author: fix issues, then 'handoff push <summary>' to re-request review" : ((state == "approved") ? "Anyone: run 'handoff merge' to merge to master" : ((state == "merged") ? "Done. Merge complete." : ((state == "abandoned") ? "Abandoned. No action needed." : "Unknown state.")))))));
    }

    public static string detect_agent()
    {
        return ((Func<string, string>)((dir) => (dir.Contains("linux") ? "linux-agent" : (dir.Contains("Linux") ? "linux-agent" : "windows-agent"))))(Directory.GetCurrentDirectory());
    }

    public static string get_current_branch()
    {
        return ((Func<string, string>)((result) => ((Func<List<string>, string>)((lines) => ((((long)lines.Count) > 0L) ? lines[(int)0L] : "unknown")))(new List<string>(result.Split("\n")))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --abbrev-ref HEAD") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))());
    }

    public static object do_handoff_show()
    {
        (File.Exists(handoff_file()) ? ((Func<string, object>)((content) => ((Func<List<string>, object>)((lines) => ((Func<string, object>)((state) => ((Func<string, object>)((branch) => ((Func<string, object>)((author) => ((Func<string, object>)((reviewer) => ((Func<string, object>)((summary) => ((Func<string, object>)((updated) => ((Func<string, object>)((notes) => Console.WriteLine(string.Concat("=== Handoff ===\n  State:    ", string.Concat(state, string.Concat("\n  Branch:   ", string.Concat(branch, string.Concat("\n  Author:   ", string.Concat(author, string.Concat("\n  Reviewer: ", string.Concat(((((long)reviewer.Length) == 0L) ? "(none yet)" : reviewer), string.Concat("\n  Summary:  ", string.Concat(summary, string.Concat("\n  Updated:  ", string.Concat(updated, string.Concat("\n  Notes:    ", string.Concat(notes, string.Concat("\n  Next:     ", string.Concat(next_action_hint(state), "\n===============")))))))))))))))))))(kv_get("notes", lines, 0L))))(kv_get("updated", lines, 0L))))(kv_get("summary", lines, 0L))))(kv_get("reviewer", lines, 0L))))(kv_get("author", lines, 0L))))(kv_get("branch", lines, 0L))))(kv_get("state", lines, 0L))))(new List<string>(content.Split("\n")))))(File.ReadAllText(handoff_file())) : Console.WriteLine("  No active handoff. Use 'codex-agent handoff push <summary>' to create one."));
        return null;
    }

    public static string make_handoff_content(string branch, string agent, string summary, string state)
    {
        return string.Concat("state=", string.Concat(state, string.Concat("\nbranch=", string.Concat(branch, string.Concat("\nauthor=", string.Concat(agent, string.Concat("\nreviewer=\nsummary=", string.Concat(summary, "\nupdated=see-git-log\nnotes=\n"))))))));
    }

    public static object do_handoff_push(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 4L) ? Console.WriteLine("Usage: codex-agent handoff push <summary>") : ((Func<string, object>)((summary) => ((Func<string, object>)((branch) => ((Func<string, object>)((agent) => ((Func<string, object>)((old_content) => ((Func<List<string>, object>)((old_lines) => ((Func<string, object>)((old_state) => ((Func<string, object>)((from_state) => ((Func<long, object>)((ok) => ((ok == 0L) ? Console.WriteLine(string.Concat("  ERROR: cannot transition from '", string.Concat(from_state, "' to 'awaiting-review'"))) : ((Func<string, object>)((new_content) => ((Func<object, object>)((_) => ((Func<string, object>)((_) => ((Func<string, object>)((_) => Console.WriteLine(string.Concat("  Handoff created: awaiting-review\n  Branch:  ", string.Concat(branch, string.Concat("\n  Author:  ", string.Concat(agent, string.Concat("\n  Summary: ", string.Concat(summary, "\n  Next: push branch to origin, then another agent runs 'handoff review'")))))))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("git", string.Concat("commit -m \"handoff: ", string.Concat(summary, "\""))) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("git", string.Concat("add ", handoff_file())) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(File.WriteAllText(handoff_file(), new_content))))(make_handoff_content(branch, agent, summary, "awaiting-review")))))(valid_transition(from_state, "awaiting-review"))))(((((long)old_state.Length) == 0L) ? "draft" : old_state))))(kv_get("state", old_lines, 0L))))(new List<string>(old_content.Split("\n")))))((File.Exists(handoff_file()) ? File.ReadAllText(handoff_file()) : ""))))(detect_agent())))(get_current_branch())))(collect_args(args, 3L, 3L)))))(((long)args.Count));
        return null;
    }

    public static object do_handoff_review(List<string> args)
    {
        (File.Exists(handoff_file()) ? ((Func<string, object>)((content) => ((Func<List<string>, object>)((lines) => ((Func<string, object>)((state) => ((Func<long, object>)((ok) => ((ok == 0L) ? Console.WriteLine(string.Concat("  ERROR: cannot review from state '", string.Concat(state, "' (need awaiting-review)"))) : ((Func<string, object>)((agent) => ((Func<string, object>)((new_content) => ((Func<object, object>)((_) => ((Func<string, object>)((branch) => ((Func<string, object>)((summary) => Console.WriteLine(string.Concat("  Review started\n  Branch:   ", string.Concat(branch, string.Concat("\n  Summary:  ", string.Concat(summary, string.Concat("\n  Reviewer: ", string.Concat(agent, "\n  Next: build, test, review code, then 'handoff approve' or 'handoff request-changes <reason>'")))))))))(kv_get("summary", lines, 0L))))(kv_get("branch", lines, 0L))))(File.WriteAllText(handoff_file(), new_content))))(kv_set("state", "under-review", kv_set("reviewer", agent, content)))))(detect_agent()))))(valid_transition(state, "under-review"))))(kv_get("state", lines, 0L))))(new List<string>(content.Split("\n")))))(File.ReadAllText(handoff_file())) : Console.WriteLine("  No handoff found. Nothing to review."));
        return null;
    }

    public static object do_handoff_approve(List<string> args)
    {
        (File.Exists(handoff_file()) ? ((Func<string, object>)((content) => ((Func<List<string>, object>)((lines) => ((Func<string, object>)((state) => ((Func<long, object>)((ok) => ((ok == 0L) ? Console.WriteLine(string.Concat("  ERROR: cannot approve from state '", string.Concat(state, "' (need under-review)"))) : ((Func<string, object>)((new_content) => ((Func<object, object>)((_) => ((Func<string, object>)((branch) => Console.WriteLine(string.Concat("  APPROVED\n  Branch: ", string.Concat(branch, "\n  Next: run 'handoff merge' to merge to master and clean up")))))(kv_get("branch", lines, 0L))))(File.WriteAllText(handoff_file(), new_content))))(kv_set("state", "approved", content)))))(valid_transition(state, "approved"))))(kv_get("state", lines, 0L))))(new List<string>(content.Split("\n")))))(File.ReadAllText(handoff_file())) : Console.WriteLine("  No handoff found."));
        return null;
    }

    public static object do_handoff_request_changes(List<string> args)
    {
        (File.Exists(handoff_file()) ? ((Func<string, object>)((content) => ((Func<List<string>, object>)((lines) => ((Func<string, object>)((state) => ((Func<long, object>)((ok) => ((ok == 0L) ? Console.WriteLine(string.Concat("  ERROR: cannot request changes from state '", string.Concat(state, "' (need under-review)"))) : ((Func<string, object>)((reason) => ((Func<string, object>)((new_content) => ((Func<object, object>)((_) => ((Func<string, object>)((branch) => Console.WriteLine(string.Concat("  CHANGES REQUESTED\n  Branch: ", string.Concat(branch, string.Concat("\n  Reason: ", string.Concat(reason, "\n  Next: author fixes issues, then 'handoff push <summary>'")))))))(kv_get("branch", lines, 0L))))(File.WriteAllText(handoff_file(), new_content))))(kv_set("state", "changes-requested", kv_set("notes", reason, content)))))(((((long)args.Count) > 3L) ? collect_args(args, 3L, 3L) : "(no reason given)")))))(valid_transition(state, "changes-requested"))))(kv_get("state", lines, 0L))))(new List<string>(content.Split("\n")))))(File.ReadAllText(handoff_file())) : Console.WriteLine("  No handoff found."));
        return null;
    }

    public static object do_handoff_merge(List<string> args)
    {
        (File.Exists(handoff_file()) ? ((Func<string, object>)((content) => ((Func<List<string>, object>)((lines) => ((Func<string, object>)((state) => ((Func<long, object>)((ok) => ((ok == 0L) ? Console.WriteLine(string.Concat("  ERROR: cannot merge from state '", string.Concat(state, "' (need approved)"))) : ((Func<string, object>)((branch) => ((Func<string, object>)((summary) => ((Func<string, object>)((author) => ((Func<string, object>)((reviewer) => ((Func<string, object>)((merge_msg) => ((Func<object, object>)((_) => ((Func<string, object>)((_) => ((Func<string, object>)((_) => Console.WriteLine(string.Concat("  MERGED to master\n  Commit: ", string.Concat(merge_msg, "\n  Next: push master")))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("git", string.Concat("merge ", string.Concat(branch, string.Concat(" --no-ff -m \"", string.Concat(merge_msg, "\""))))) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("git", "checkout master") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(File.WriteAllText(handoff_file(), kv_set("state", "merged", content)))))(string.Concat("merge: ", string.Concat(branch, string.Concat(" -- ", string.Concat(summary, string.Concat(" (author: ", string.Concat(author, string.Concat(", reviewer: ", string.Concat(reviewer, ")")))))))))))(kv_get("reviewer", lines, 0L))))(kv_get("author", lines, 0L))))(kv_get("summary", lines, 0L))))(kv_get("branch", lines, 0L)))))(valid_transition(state, "merged"))))(kv_get("state", lines, 0L))))(new List<string>(content.Split("\n")))))(File.ReadAllText(handoff_file())) : Console.WriteLine("  No handoff found."));
        return null;
    }

    public static object do_handoff_abandon(List<string> args)
    {
        (File.Exists(handoff_file()) ? ((Func<string, object>)((content) => ((Func<List<string>, object>)((lines) => ((Func<string, object>)((state) => ((Func<string, object>)((new_content) => ((Func<object, object>)((_) => ((Func<string, object>)((branch) => Console.WriteLine(string.Concat("  ABANDONED\n  Branch: ", string.Concat(branch, string.Concat("\n  Previous state: ", state))))))(kv_get("branch", lines, 0L))))(File.WriteAllText(handoff_file(), new_content))))(kv_set("state", "abandoned", content))))(kv_get("state", lines, 0L))))(new List<string>(content.Split("\n")))))(File.ReadAllText(handoff_file())) : Console.WriteLine("  No handoff found."));
        return null;
    }

    public static object do_handoff(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? do_handoff_show() : ((Func<string, object>)((action) => ((action == "show") ? do_handoff_show() : ((action == "push") ? do_handoff_push(args) : ((action == "review") ? do_handoff_review(args) : ((action == "approve") ? do_handoff_approve(args) : ((action == "request-changes") ? do_handoff_request_changes(args) : ((action == "merge") ? do_handoff_merge(args) : ((action == "abandon") ? do_handoff_abandon(args) : Console.WriteLine(string.Concat("Unknown handoff action: ", string.Concat(action, ". Use show, push, review, approve, request-changes, merge, or abandon."))))))))))))(args[(int)2L]))))(((long)args.Count));
        return null;
    }

    public static long count_error_lines(List<string> lines, long idx, long acc)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return acc;
            }
            else
            {
                var line = lines[(int)idx];
                var has_error = line.Contains(": error ");
                var _tco_0 = lines;
                var _tco_1 = (idx + 1L);
                var _tco_2 = (has_error ? (acc + 1L) : acc);
                lines = _tco_0;
                idx = _tco_1;
                acc = _tco_2;
                continue;
            }
        }
    }

    public static long count_warning_lines(List<string> lines, long idx, long acc)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return acc;
            }
            else
            {
                var line = lines[(int)idx];
                var has_warn = line.Contains(": warning ");
                var _tco_0 = lines;
                var _tco_1 = (idx + 1L);
                var _tco_2 = (has_warn ? (acc + 1L) : acc);
                lines = _tco_0;
                idx = _tco_1;
                acc = _tco_2;
                continue;
            }
        }
    }

    public static string first_error_line(List<string> lines, long idx)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return "";
            }
            else
            {
                var line = lines[(int)idx];
                if (line.Contains(": error "))
                {
                    return line;
                }
                else
                {
                    var _tco_0 = lines;
                    var _tco_1 = (idx + 1L);
                    lines = _tco_0;
                    idx = _tco_1;
                    continue;
                }
            }
        }
    }

    public static long has_succeeded(List<string> lines, long idx)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return 0L;
            }
            else
            {
                var line = lines[(int)idx];
                if (line.Contains("Build succeeded"))
                {
                    return 1L;
                }
                else
                {
                    if (line.Contains("succeeded"))
                    {
                        if (line.Contains("0 failed"))
                        {
                            return 1L;
                        }
                        else
                        {
                            var _tco_0 = lines;
                            var _tco_1 = (idx + 1L);
                            lines = _tco_0;
                            idx = _tco_1;
                            continue;
                        }
                    }
                    else
                    {
                        var _tco_0 = lines;
                        var _tco_1 = (idx + 1L);
                        lines = _tco_0;
                        idx = _tco_1;
                        continue;
                    }
                }
            }
        }
    }

    public static long has_test_failure(List<string> lines, long idx)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return 0L;
            }
            else
            {
                var line = lines[(int)idx];
                if (line.Contains("Failed!"))
                {
                    return 1L;
                }
                else
                {
                    var _tco_0 = lines;
                    var _tco_1 = (idx + 1L);
                    lines = _tco_0;
                    idx = _tco_1;
                    continue;
                }
            }
        }
    }

    public static long has_any_test_result(List<string> lines, long idx)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return 0L;
            }
            else
            {
                var line = lines[(int)idx];
                if (line.Contains("Passed!"))
                {
                    return 1L;
                }
                else
                {
                    if (line.Contains("Failed!"))
                    {
                        return 1L;
                    }
                    else
                    {
                        var _tco_0 = lines;
                        var _tco_1 = (idx + 1L);
                        lines = _tco_0;
                        idx = _tco_1;
                        continue;
                    }
                }
            }
        }
    }

    public static string count_test_passed(List<string> lines, long idx)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return "?";
            }
            else
            {
                var line = lines[(int)idx];
                if (line.Contains("Passed"))
                {
                    return line;
                }
                else
                {
                    if (line.Contains("Failed"))
                    {
                        return line;
                    }
                    else
                    {
                        var _tco_0 = lines;
                        var _tco_1 = (idx + 1L);
                        lines = _tco_0;
                        idx = _tco_1;
                        continue;
                    }
                }
            }
        }
    }

    public static long count_condition_headers(List<string> lines, long idx, long acc)
    {
        while (true)
        {
            if ((idx >= ((long)lines.Count)))
            {
                return acc;
            }
            else
            {
                var line = lines[(int)idx];
                var is_header = line.StartsWith("### ");
                var _tco_0 = lines;
                var _tco_1 = (idx + 1L);
                var _tco_2 = (is_header ? (acc + 1L) : acc);
                lines = _tco_0;
                idx = _tco_1;
                acc = _tco_2;
                continue;
            }
        }
    }

    public static string collect_condition_summaries(List<string> lines, long idx)
    {
        return ((idx >= ((long)lines.Count)) ? "" : ((Func<string, string>)((line) => ((Func<string, string>)((rest) => (line.StartsWith("### ") ? string.Concat("  * ", string.Concat(line, string.Concat("\n", rest))) : rest)))(collect_condition_summaries(lines, (idx + 1L)))))(lines[(int)idx]));
    }

    public static object do_doctor(List<string> args)
    {
        ((Func<string, object>)((kc_status) => ((Func<string, object>)((kc_body) => ((Func<List<string>, object>)((kc_lines) => ((Func<long, object>)((n_conditions) => ((Func<string, object>)((summaries) => ((Func<string, object>)((cs5001_check) => ((Func<string, object>)((session_status) => ((Func<string, object>)((last_build_status) => ((Func<string, object>)((last_test_status) => ((Func<string, object>)((handoff_status) => Console.WriteLine(string.Concat("=== Doctor -- Known Conditions & Diagnostics ===\n", string.Concat("  Conditions file: ", string.Concat(kc_status, string.Concat(" (", string.Concat((n_conditions).ToString(), string.Concat(" known)\n", string.Concat(summaries, string.Concat(cs5001_check, string.Concat("\n", string.Concat(session_status, string.Concat("\n", string.Concat(last_build_status, string.Concat("\n", string.Concat(last_test_status, string.Concat("\n", string.Concat(handoff_status, string.Concat("\n", string.Concat("  Advice: CS5001 in Codex.Codex is PRE-EXISTING. Do NOT investigate.\n", string.Concat("  Advice: Do NOT extract effectful helper functions in .codex -- inline instead.\n", string.Concat("  Advice: After editing .codex files, check for else->then corruption.\n", string.Concat("  See: docs/KNOWN-CONDITIONS.md for full details.\n", "================================================"))))))))))))))))))))))))((File.Exists(handoff_file()) ? ((Func<string, string>)((hc) => ((Func<List<string>, string>)((hl) => ((Func<string, string>)((hs) => ((Func<string, string>)((hb) => string.Concat("  Handoff: ", string.Concat(hs, string.Concat(" on ", string.Concat(hb, string.Concat(" -- ", next_action_hint(hs))))))))(kv_get("branch", hl, 0L))))(kv_get("state", hl, 0L))))(new List<string>(hc.Split("\n")))))(File.ReadAllText(handoff_file())) : "  Handoff: (none active)"))))((File.Exists(test_log()) ? ((Func<string, string>)((tc) => ((Func<List<string>, string>)((tl) => ((Func<string, string>)((tp) => string.Concat("  Last test: ", string.Concat(tp, string.Concat(" (see ", string.Concat(test_log(), ")"))))))(count_test_passed(tl, 0L))))(new List<string>(tc.Split("\n")))))(File.ReadAllText(test_log())) : "  Last test: (no record)"))))((File.Exists(build_log()) ? ((Func<string, string>)((bc) => ((Func<List<string>, string>)((bl) => ((Func<long, string>)((be) => string.Concat("  Last build: ", string.Concat((be).ToString(), string.Concat(" errors (see ", string.Concat(build_log(), ")"))))))(count_error_lines(bl, 0L, 0L))))(new List<string>(bc.Split("\n")))))(File.ReadAllText(build_log())) : "  Last build: (no record)"))))((File.Exists(log_file()) ? ((Func<string, string>)((log_content) => ((Func<long, string>)((log_lines) => string.Concat("  Session log: ", string.Concat((log_lines).ToString(), " entries"))))(count_lines(log_content))))(File.ReadAllText(log_file())) : "  Session log: (none -- new session)"))))((File.Exists("Codex.Codex/Codex.Codex.csproj") ? ((Func<string, string>)((csproj) => (csproj.Contains("<OutputType>Exe</OutputType>") ? "  CS5001 trap: ACTIVE (Codex.Codex is Exe, will emit CS5001 -- this is expected)" : "  CS5001 trap: cleared")))(File.ReadAllText("Codex.Codex/Codex.Codex.csproj")) : "  CS5001 trap: n/a (no Codex.Codex project)"))))(collect_condition_summaries(kc_lines, 0L))))(count_condition_headers(kc_lines, 0L, 0L))))(((((long)kc_body.Length) > 0L) ? new List<string>(kc_body.Split("\n")) : new List<string>("".Split("\n"))))))((File.Exists(known_conditions_file()) ? File.ReadAllText(known_conditions_file()) : ""))))((File.Exists(known_conditions_file()) ? "found" : "MISSING"));
        return null;
    }

    public static object do_log(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? Console.WriteLine("Usage: codex-agent log <message>") : ((Func<string, object>)((msg) => ((Func<string, object>)((existing) => ((Func<string, object>)((entry) => ((Func<object, object>)((_) => Console.WriteLine(string.Concat("  Logged: ", msg))))(File.WriteAllText(log_file(), string.Concat(existing, string.Concat(entry, "\n"))))))(string.Concat("[LOG] ", msg))))((File.Exists(log_file()) ? File.ReadAllText(log_file()) : ""))))(collect_args(args, 2L, 2L)))))(((long)args.Count));
        return null;
    }

    public static string take_last_n(List<string> lines, long idx, long start, long total)
    {
        while (true)
        {
            if ((idx >= total))
            {
                return "";
            }
            else
            {
                if ((idx < start))
                {
                    var _tco_0 = lines;
                    var _tco_1 = (idx + 1L);
                    var _tco_2 = start;
                    var _tco_3 = total;
                    lines = _tco_0;
                    idx = _tco_1;
                    start = _tco_2;
                    total = _tco_3;
                    continue;
                }
                else
                {
                    var line = lines[(int)idx];
                    var rest = take_last_n(lines, (idx + 1L), start, total);
                    if ((((long)rest.Length) == 0L))
                    {
                        return line;
                    }
                    else
                    {
                        return string.Concat(line, string.Concat("\n", rest));
                    }
                }
            }
        }
    }

    public static object do_recall(List<string> args)
    {
        (File.Exists(log_file()) ? ((Func<string, object>)((content) => ((((long)content.Length) == 0L) ? Console.WriteLine("  (session log is empty)") : ((Func<List<string>, object>)((lines) => ((Func<long, object>)((raw_total) => ((Func<long, object>)((total) => ((Func<long, object>)((n) => ((Func<long, object>)((start) => Console.WriteLine(string.Concat("=== Session Log (last ", string.Concat((n).ToString(), string.Concat(") ===\n", string.Concat(take_last_n(lines, 0L, start, total), "\n===========================")))))))(((total > n) ? (total - n) : 0L))))(((((long)args.Count) > 2L) ? parse_int_or(args[(int)2L], 10L) : 10L))))(((raw_total > 0L) ? (raw_total - 1L) : 0L))))(((long)lines.Count))))(new List<string>(content.Split("\n"))))))(File.ReadAllText(log_file())) : Console.WriteLine("  (no session log yet. Use 'codex-agent log <msg>' or run build/test.)"));
        return null;
    }

    public static object do_plan_show()
    {
        (File.Exists(plan_file()) ? ((Func<string, object>)((content) => ((((long)content.Length) == 0L) ? Console.WriteLine("  (no tasks)") : Console.WriteLine(string.Concat("=== Agent Plan ===\n", string.Concat(content, "=================="))))))(File.ReadAllText(plan_file())) : Console.WriteLine("  (no plan file yet. Use 'codex-agent plan add <task>' to start.)"));
        return null;
    }

    public static string do_plan_add(List<string> args, long idx)
    {
        return ((idx >= ((long)args.Count)) ? "" : ((idx == 3L) ? string.Concat(args[(int)idx], do_plan_add(args, (idx + 1L))) : string.Concat(" ", string.Concat(args[(int)idx], do_plan_add(args, (idx + 1L))))));
    }

    public static object do_plan(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? do_plan_show() : ((Func<string, object>)((action) => ((action == "show") ? do_plan_show() : ((action == "clear") ? ((Func<object, object>)((_) => Console.WriteLine("  Plan cleared.")))(File.WriteAllText(plan_file(), "")) : ((action == "add") ? ((argc < 4L) ? Console.WriteLine("Usage: codex-agent plan add <task description>") : ((Func<string, object>)((task) => ((Func<string, object>)((existing) => ((Func<string, object>)((new_content) => ((Func<object, object>)((_) => Console.WriteLine(string.Concat("  Added: ", task))))(File.WriteAllText(plan_file(), new_content))))(((((long)existing.Length) == 0L) ? string.Concat("- ", string.Concat(task, "\n")) : string.Concat(existing, string.Concat("- ", string.Concat(task, "\n")))))))((File.Exists(plan_file()) ? File.ReadAllText(plan_file()) : ""))))(do_plan_add(args, 3L))) : Console.WriteLine(string.Concat("Unknown plan action: ", string.Concat(action, ". Use add, show, or clear."))))))))(args[(int)2L]))))(((long)args.Count));
        return null;
    }

    public static long agent_budget(string name)
    {
        return ((name == "cam") ? 800000L : ((name == "windows") ? 60000L : ((name == "linux") ? 60000L : 60000L)));
    }

    public static string agent_label(string name)
    {
        return ((name == "cam") ? "Cam (Claude Code CLI, 1M context)" : ((name == "windows") ? "Agent Windows (Copilot/VS)" : ((name == "linux") ? "Agent Linux (Claude sandbox)" : "unknown agent (using default 60K budget)")));
    }

    public static string check_level(long pct)
    {
        return ((pct > 90L) ? "RED - overloaded, build and verify NOW" : ((pct > 70L) ? "ORANGE - high load, finish current task then build" : ((pct > 40L) ? "YELLOW - moderate, stay focused" : "GREEN - plenty of headroom")));
    }

    public static object do_check(List<string> args)
    {
        ((Func<long, object>)((argc) => ((Func<string, object>)((name) => ((Func<long, object>)((budget) => ((Func<string, object>)((label) => ((Func<string, object>)((pc) => ((Func<long, object>)((parser_chars) => ((Func<string, object>)((tc) => ((Func<long, object>)((tc_chars) => ((Func<string, object>)((ec) => ((Func<long, object>)((emitter_chars) => ((Func<string, object>)((lc) => ((Func<long, object>)((lowering_chars) => ((Func<string, object>)((uc) => ((Func<long, object>)((unifier_chars) => ((Func<string, object>)((xc) => ((Func<long, object>)((lexer_chars) => ((Func<long, object>)((total_hot) => ((Func<long, object>)((pct) => ((Func<string, object>)((level) => ((Func<string, object>)((plan_status) => Console.WriteLine(string.Concat("=== Cognitive Check ===\n", string.Concat("  Agent:  ", string.Concat(label, string.Concat("\n", string.Concat("  Hot path files: ", string.Concat((total_hot).ToString(), string.Concat(" chars / ", string.Concat((budget).ToString(), string.Concat(" budget (", string.Concat((pct).ToString(), string.Concat("%)\n", string.Concat("  Load: ", string.Concat(level, string.Concat("\n", string.Concat("  Parser.codex:       ", string.Concat(pad_left((parser_chars).ToString(), 6L), string.Concat(" chars\n", string.Concat("  TypeChecker.codex:  ", string.Concat(pad_left((tc_chars).ToString(), 6L), string.Concat(" chars\n", string.Concat("  CSharpEmitter.codex:", string.Concat(pad_left((emitter_chars).ToString(), 6L), string.Concat(" chars\n", string.Concat("  Lowering.codex:     ", string.Concat(pad_left((lowering_chars).ToString(), 6L), string.Concat(" chars\n", string.Concat("  Unifier.codex:      ", string.Concat(pad_left((unifier_chars).ToString(), 6L), string.Concat(" chars\n", string.Concat("  Lexer.codex:        ", string.Concat(pad_left((lexer_chars).ToString(), 6L), string.Concat(" chars\n", string.Concat(plan_status, string.Concat("\n", "=======================")))))))))))))))))))))))))))))))))))))((File.Exists(plan_file()) ? ((Func<string, string>)((planc) => ((((long)planc.Length) == 0L) ? "  Plan: (empty)" : string.Concat("  Plan: ", string.Concat(((((long)new List<string>(planc.Split("\n")).Count) - 1L)).ToString(), " tasks")))))(File.ReadAllText(plan_file())) : "  Plan: (none)"))))(check_level(pct))))(((total_hot * 100L) / budget))))((((((parser_chars + tc_chars) + emitter_chars) + lowering_chars) + unifier_chars) + lexer_chars))))(((long)xc.Length))))((File.Exists("Codex.Codex/Syntax/Lexer.codex") ? File.ReadAllText("Codex.Codex/Syntax/Lexer.codex") : ""))))(((long)uc.Length))))((File.Exists("Codex.Codex/Types/Unifier.codex") ? File.ReadAllText("Codex.Codex/Types/Unifier.codex") : ""))))(((long)lc.Length))))((File.Exists("Codex.Codex/IR/Lowering.codex") ? File.ReadAllText("Codex.Codex/IR/Lowering.codex") : ""))))(((long)ec.Length))))((File.Exists("Codex.Codex/Emit/CSharpEmitter.codex") ? File.ReadAllText("Codex.Codex/Emit/CSharpEmitter.codex") : ""))))(((long)tc.Length))))((File.Exists("Codex.Codex/Types/TypeChecker.codex") ? File.ReadAllText("Codex.Codex/Types/TypeChecker.codex") : ""))))(((long)pc.Length))))((File.Exists("Codex.Codex/Syntax/Parser.codex") ? File.ReadAllText("Codex.Codex/Syntax/Parser.codex") : ""))))(agent_label(name))))(agent_budget(name))))(((argc > 2L) ? args[(int)2L] : "unknown"))))(((long)args.Count));
        return null;
    }

    public static object do_build(List<string> args)
    {
        ((Func<string, object>)((output) => ((Func<object, object>)((_) => ((Func<List<string>, object>)((lines) => ((Func<long, object>)((errs) => ((Func<long, object>)((warns) => ((Func<long, object>)((ok) => ((Func<string, object>)((status) => ((Func<string, object>)((first_err) => ((Func<string, object>)((summary) => ((Func<string, object>)((log_entry) => ((Func<string, object>)((log_existing) => ((Func<object, object>)((_) => ((Func<string, object>)((verify_output) => Console.WriteLine(string.Concat(summary, string.Concat("\n", verify_output)))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("pwsh", "-File tools/codex-agent-verify.ps1 docs/CurrentPlan.md docs/ToDo/CSharpCleanup.md docs/TOOL-ERROR-REGISTRY.md docs/KNOWN-CONDITIONS.md .github/copilot-instructions.md") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(File.WriteAllText(log_file(), string.Concat(log_existing, string.Concat(log_entry, "\n"))))))((File.Exists(log_file()) ? File.ReadAllText(log_file()) : ""))))(string.Concat("[BUILD] ", string.Concat(status, string.Concat(" errors=", string.Concat((errs).ToString(), string.Concat(" warnings=", (warns).ToString()))))))))(string.Concat("=== Build ", string.Concat(status, string.Concat(" === errors: ", string.Concat((errs).ToString(), string.Concat("  warnings: ", string.Concat((warns).ToString(), string.Concat(first_err, string.Concat("\n  Full log: ", build_log())))))))))))(((errs > 0L) ? string.Concat("\n  First error: ", first_error_line(lines, 0L)) : ""))))(((ok == 1L) ? "PASS" : "FAIL"))))(has_succeeded(lines, 0L))))(count_warning_lines(lines, 0L, 0L))))(count_error_lines(lines, 0L, 0L))))(new List<string>(output.Split("\n")))))(File.WriteAllText(build_log(), output))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("dotnet", "build Codex.sln --verbosity quiet") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))());
        return null;
    }

    public static object do_test(List<string> args)
    {
        ((Func<long, object>)((argc) => ((Func<string, object>)((extra) => ((Func<string, object>)((output) => ((Func<object, object>)((_) => ((Func<List<string>, object>)((lines) => ((Func<long, object>)((errs) => ((Func<string, object>)((test_line) => ((Func<long, object>)((has_tests) => ((Func<long, object>)((has_fail) => ((Func<string, object>)((status) => ((Func<string, object>)((first_err) => ((Func<string, object>)((summary) => ((Func<string, object>)((log_entry) => ((Func<string, object>)((log_existing) => ((Func<object, object>)((_) => Console.WriteLine(summary)))(File.WriteAllText(log_file(), string.Concat(log_existing, string.Concat(log_entry, "\n"))))))((File.Exists(log_file()) ? File.ReadAllText(log_file()) : ""))))(string.Concat("[TEST] ", string.Concat(status, string.Concat(" ", test_line))))))(string.Concat("=== Test ", string.Concat(status, string.Concat(" === ", string.Concat(test_line, string.Concat(first_err, string.Concat("\n  Full log: ", test_log())))))))))(((errs > 0L) ? string.Concat("\n  First error: ", first_error_line(lines, 0L)) : ""))))(((errs > 0L) ? "BUILD-FAIL" : ((has_tests == 1L) ? ((has_fail == 1L) ? "FAIL" : "PASS") : "FAIL")))))(has_test_failure(lines, 0L))))(has_any_test_result(lines, 0L))))(count_test_passed(lines, 0L))))(count_error_lines(lines, 0L, 0L))))(new List<string>(output.Split("\n")))))(File.WriteAllText(test_log(), output))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("dotnet", string.Concat("test Codex.sln --verbosity quiet", extra)) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((argc > 2L) ? string.Concat(" --filter ", args[(int)2L]) : ""))))(((long)args.Count));
        return null;
    }

    public static object print_help()
    {
        Console.WriteLine(string.Concat("codex-agent - Codex compiler toolkit for AI agents\n", string.Concat("\n", string.Concat("Commands:\n", string.Concat("  peek <file> [start] [end]          Read file lines safely\n", string.Concat("  stat <file> [file2] ...            File statistics (lines, chars)\n", string.Concat("  snap save <file>                   Snapshot before editing\n", string.Concat("  snap diff <file>                   Compare file to snapshot\n", string.Concat("  snap restore <file>                Restore from snapshot\n", string.Concat("  status                             Project status check\n", string.Concat("  plan [add <task>|show|clear]       Task list (survives context loss)\n", string.Concat("  check [agent]                      Cognitive load estimate (cam|windows|linux)\n", string.Concat("  doctor                             Known conditions & diagnostics briefing\n", string.Concat("  handoff [show]                     Show current handoff state\n", string.Concat("  handoff push <summary>             Create/update handoff, request review\n", string.Concat("  handoff review                     Pick up handoff for review\n", string.Concat("  handoff approve                    Approve after review\n", string.Concat("  handoff request-changes <reason>   Request changes with reason\n", string.Concat("  handoff merge                      Merge approved branch to master\n", string.Concat("  handoff abandon                    Abandon handoff\n", string.Concat("  log <message>                      Append to session log\n", string.Concat("  recall [n]                         Show last N session log entries (default 10)\n", string.Concat("  build                              Build + compact summary (full log stored)\n", string.Concat("  test [filter]                      Test + compact summary (full log stored)\n", string.Concat("  orient [topic]                      Project map (topics: syntax, pipeline, agents, style, git, tools, structure, pitfalls)\n", string.Concat("  greet <name> [--context N] [...]    Register agent identity and capabilities\n", string.Concat("  roster                              Show registered agents and capabilities\n", string.Concat("  init                                Full session start (doctor+check+status+handoff+sweep+git)\n", string.Concat("  help                                This message\n", string.Concat("\n", string.Concat("Handoff lifecycle: draft -> awaiting-review -> under-review -> approved -> merged\n", string.Concat("                                             \\-> changes-requested -> awaiting-review\n", string.Concat("\n", string.Concat("Usage: codex-agent <command> [args]\n", "Built from codex-agent.codex via the Codex IL backend."))))))))))))))))))))))))))))))))));
        return null;
    }

    public static string agents_dir()
    {
        return ".codex-agent/agents";
    }

    public static string agent_file(string name)
    {
        return string.Concat(agents_dir(), string.Concat("/", string.Concat(name, ".txt")));
    }

    public static object do_greet(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? Console.WriteLine("Usage: codex-agent greet <name> <context> <tools> <os> <workdir>") : ((Func<string, object>)((name) => ((Func<string, object>)((_) => ((Func<string, object>)((ctx) => ((Func<string, object>)((tools_val) => ((Func<string, object>)((os_val) => ((Func<string, object>)((workdir_val) => ((Func<string, object>)((dotnet_ver) => ((Func<string, object>)((content) => ((Func<object, object>)((_) => Console.WriteLine(string.Concat("  Agent registered: ", string.Concat(name, string.Concat("\n  Context: ", string.Concat(((((long)ctx.Length) > 0L) ? ctx : "(default)"), string.Concat("\n  Tools: ", string.Concat(((((long)tools_val.Length) > 0L) ? tools_val : "(none)"), string.Concat("\n  OS: ", string.Concat(((((long)os_val.Length) > 0L) ? os_val : "(unknown)"), string.Concat("\n  Workdir: ", workdir_val))))))))))))(File.WriteAllText(agent_file(name), content))))(string.Concat("agent=", string.Concat(name, string.Concat("\ncontext=", string.Concat(ctx, string.Concat("\ntools=", string.Concat(tools_val, string.Concat("\nos=", string.Concat(os_val, string.Concat("\nworkdir=", string.Concat(workdir_val, string.Concat("\ndotnet=", string.Concat(dotnet_ver, "last-seen=see-git-log\n")))))))))))))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((argc > 6L) ? args[(int)6L] : Directory.GetCurrentDirectory()))))(((argc > 5L) ? args[(int)5L] : ""))))(((argc > 4L) ? args[(int)4L] : ""))))(((argc > 3L) ? args[(int)3L] : ""))))((File.Exists(agents_dir()) ? "" : ((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("pwsh", string.Concat("-Command New-Item -ItemType Directory -Force -Path ", agents_dir())) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))()))))(args[(int)2L]))))(((long)args.Count));
        return null;
    }

    public static string format_agent_entry(string content)
    {
        return ((Func<List<string>, string>)((lines) => ((Func<string, string>)((name) => ((Func<string, string>)((ctx) => ((Func<string, string>)((tools_val) => ((Func<string, string>)((os_val) => ((Func<string, string>)((workdir_val) => string.Concat("  ", string.Concat(pad_right(name, 16L), string.Concat(pad_right(((((long)ctx.Length) > 0L) ? ctx : "?"), 44L), string.Concat(pad_right(os_val, 40L), string.Concat(pad_right(tools_val, 44L), workdir_val)))))))(kv_get("workdir", lines, 0L))))(kv_get("os", lines, 0L))))(kv_get("tools", lines, 0L))))(kv_get("context", lines, 0L))))(kv_get("agent", lines, 0L))))(new List<string>(content.Split("\n")));
    }

    public static string format_roster_loop(List<string> files, long idx)
    {
        return ((idx >= ((long)files.Count)) ? "" : ((Func<string, string>)((path) => ((Func<string, string>)((content) => ((Func<string, string>)((entry) => ((Func<string, string>)((rest) => ((((long)rest.Length) == 0L) ? entry : string.Concat(entry, string.Concat("\n", rest)))))(format_roster_loop(files, (idx + 1L)))))(format_agent_entry(content))))(File.ReadAllText(path))))(files[(int)idx]));
    }

    public static object do_roster(List<string> args)
    {
        ((Func<List<string>, object>)((files) => ((((long)files.Count) == 0L) ? Console.WriteLine("  No agents registered. Use 'codex-agent greet <name>' to register.") : ((Func<string, object>)((header) => ((Func<string, object>)((entries) => Console.WriteLine(string.Concat(header, string.Concat("\n", string.Concat(entries, "\n====================="))))))(format_roster_loop(files, 0L))))("=== Agent Roster ==="))))(new List<string>(Directory.GetFiles(agents_dir(), "*.txt")));
        return null;
    }

    public static object orient_level0()
    {
        ((Func<string, object>)((lock_status) => ((Func<List<string>, object>)((roster_files) => ((Func<string, object>)((roster_info) => ((Func<string, object>)((plan_info) => Console.WriteLine(string.Concat("=== Codex Project — Quick Orientation ===\n", string.Concat("\n", string.Concat("  Language:  Self-hosting compiler, .codex source, ML-family syntax\n", string.Concat("  Pipeline:  Lexer -> Parser -> Desugarer -> NameResolver -> TypeChecker -> Lowering -> Emitter\n", string.Concat("  Backends:  15 (C#, JS, Python, Rust, C++, Go, Java, Ada, Fortran, COBOL, IL, RISC-V, ARM64, x86-64, WASM)\n", string.Concat("  Tests:     900+, run with: dotnet test Codex.sln\n", string.Concat("  Build:     dotnet build Codex.sln (.NET 8, TreatWarningsAsErrors)\n", string.Concat("  Self-host: 26 .codex files in Codex.Codex/\n", string.Concat("\n", string.Concat("  Ref compiler lock: ", string.Concat(lock_status, string.Concat("\n", string.Concat("\n", string.Concat("  Agents:\n", string.Concat(roster_info, string.Concat("\n", string.Concat("\n", string.Concat(plan_info, string.Concat("\n", string.Concat("\n", string.Concat("  Key docs:\n", string.Concat("    Overview:     docs/00-OVERVIEW.md\n", string.Concat("    Principles:   docs/10-PRINCIPLES.md\n", string.Concat("    Syntax:       docs/SYNTAX-QUICKREF.md\n", string.Concat("    Pitfalls:     docs/KNOWN-CONDITIONS.md\n", string.Concat("    Tool errors:  docs/TOOL-ERROR-REGISTRY.md\n", string.Concat("    Code style:   .github/copilot-instructions.md\n", string.Concat("    Route map:    docs/Milestones/THE-LAST-PEAK.md\n", string.Concat("\n", string.Concat("  Project structure:\n", string.Concat("    Core -> Syntax -> Ast -> Semantics -> Types -> IR -> Emit -> Emit.* -> Cli\n", string.Concat("    Codex.Core has zero deps (root). Codex.Cli references everything.\n", string.Concat("\n", string.Concat("  Topics (use 'orient <topic>' for detail):\n", string.Concat("    syntax, pipeline, agents, style, git, tools, structure, pitfalls\n", string.Concat("\n", "=========================================")))))))))))))))))))))))))))))))))))))))((File.Exists("docs/CurrentPlan.md") ? ((Func<string, string>)((pc) => ((Func<List<string>, string>)((pl) => string.Concat("  Current plan: docs/CurrentPlan.md (", string.Concat((((long)pl.Count)).ToString(), " lines)"))))(new List<string>(pc.Split("\n")))))(File.ReadAllText("docs/CurrentPlan.md")) : "  Current plan: (none)"))))(((((long)roster_files.Count) > 0L) ? format_roster_loop(roster_files, 0L) : "  (none registered -- use 'codex-agent greet <name>')"))))(new List<string>(Directory.GetFiles(agents_dir(), "*.txt")))))((File.Exists("docs/Compiler/REFERENCE-COMPILER-LOCK.md") ? ((Func<string, string>)((lock_content) => (lock_content.Contains("LIFTED") ? "LIFTED — src/ is modifiable" : (lock_content.Contains("LOCKED") ? "LOCKED — do not modify src/" : "see docs/REFERENCE-COMPILER-LOCK.md"))))(File.ReadAllText("docs/Compiler/REFERENCE-COMPILER-LOCK.md")) : "unknown"));
        return null;
    }

    public static object orient_topic(string topic)
    {
        ((topic == "syntax") ? (File.Exists("docs/SYNTAX-QUICKREF.md") ? Console.WriteLine(File.ReadAllText("docs/SYNTAX-QUICKREF.md")) : Console.WriteLine("  docs/SYNTAX-QUICKREF.md not found")) : ((topic == "pitfalls") ? (File.Exists("docs/KNOWN-CONDITIONS.md") ? Console.WriteLine(File.ReadAllText("docs/KNOWN-CONDITIONS.md")) : Console.WriteLine("  docs/KNOWN-CONDITIONS.md not found")) : ((topic == "style") ? Console.WriteLine(string.Concat("=== Code Style Quick Reference ===\n", string.Concat("\n", string.Concat("  C#:\n", string.Concat("    Private fields: m_ prefix (m_diagnostics)\n", string.Concat("    Properties/types: PascalCase. Locals: camelCase.\n", string.Concat("    sealed record for immutable ref. readonly record struct for values.\n", string.Concat("    No XML doc comments (///). No var. No unused fields.\n", string.Concat("    Prefer Map<K,V> (Codex.Core) over ImmutableDictionary.\n", string.Concat("    4 spaces, UTF-8, max 120 chars/line. TreatWarningsAsErrors.\n", string.Concat("\n", string.Concat("  Codex (.codex):\n", string.Concat("    Booleans: True / False (capital T/F)\n", string.Concat("    Application: left-assoc, f x y = (f x) y\n", string.Concat("    Pattern matching: when/if, not match/case\n", string.Concat("    All functions curried. Effects in brackets: [Console] Nothing\n", string.Concat("    Do NOT extract effectful helper functions — inline instead.\n", string.Concat("\n", string.Concat("  Full details: .github/copilot-instructions.md\n", "================================="))))))))))))))))))) : ((topic == "agents") ? ((Func<List<string>, object>)((agent_files) => ((((long)agent_files.Count) > 0L) ? ((Func<string, object>)((entries) => Console.WriteLine(string.Concat("=== Agent Roster (detailed) ===\n", string.Concat(entries, string.Concat("\n\n  Register: codex-agent greet <name> <context> <tools> <os> <workdir>\n", string.Concat("  Branch prefixes: windows/, linux/, cam/, nut/\n", string.Concat("  No agent merges own work to master without review.\n", "=============================="))))))))(format_roster_loop(agent_files, 0L)) : Console.WriteLine("  No agents registered. Use 'codex-agent greet <name>'."))))(new List<string>(Directory.GetFiles(agents_dir(), "*.txt"))) : ((topic == "git") ? Console.WriteLine(string.Concat("=== Git Workflow ===\n", string.Concat("  Branch naming: windows/<topic>, linux/<topic>, cam/<topic>, nut/<topic>\n", string.Concat("  Conventional Commits: feat:, fix:, docs:, test:, chore:, refactor:\n", string.Concat("  No agent merges own work to master without review.\n", string.Concat("  Handoff: codex-agent handoff push/review/approve/merge\n", "===================")))))) : ((topic == "tools") ? Console.WriteLine(string.Concat("=== Agent Toolkit ===\n", string.Concat("  All tools run natively: tools/codex-agent/<tool>.exe <args>\n", string.Concat("  Or via dotnet:          dotnet tools/codex-agent/<tool>.exe <args>\n", string.Concat("\n", string.Concat("  codex-agent  Main toolkit (init, build, test, doctor, handoff, etc.)\n", string.Concat("  peek         Read file lines with line numbers\n", string.Concat("  fstat        File statistics\n", string.Concat("  sdiff        Side-by-side diff\n", string.Concat("\n", string.Concat("  Run 'codex-agent help' for full command list.\n", "===================="))))))))))) : ((topic == "structure") ? Console.WriteLine(string.Concat("=== Project Structure ===\n", string.Concat("  Dependency flow (one way):\n", string.Concat("    Core -> Syntax -> Ast -> Semantics -> Types -> IR -> Emit -> Emit.* -> Cli\n", string.Concat("\n", string.Concat("  Codex.Core      Zero deps (root). Map<K,V>, ValueMap, SourceSpan.\n", string.Concat("  Codex.Syntax    Lexer + Parser. Token, SyntaxTree.\n", string.Concat("  Codex.Ast       AST nodes. Expression, Declaration.\n", string.Concat("  Codex.Semantics Name resolution. NameResolver, Scope.\n", string.Concat("  Codex.Types     Type checking + inference. TypeChecker, Unifier.\n", string.Concat("  Codex.IR        IR lowering. IRModule, IRNode.\n", string.Concat("  Codex.Emit      Emitter interface. ICodeEmitter.\n", string.Concat("  Codex.Emit.*    15 backend emitters (CSharp, IL, RiscV, Arm64, etc.)\n", string.Concat("  Codex.Cli       Composition root. build/run/repl/bootstrap commands.\n", string.Concat("\n", string.Concat("  Self-hosted: 26 .codex files in Codex.Codex/\n", string.Concat("  Bootstrap:   tools/Codex.Bootstrap/\n", string.Concat("  Tests:       Codex.Core.Tests, Codex.Syntax.Tests, etc.\n", "========================")))))))))))))))))) : ((topic == "pipeline") ? Console.WriteLine(string.Concat("=== Compiler Pipeline ===\n", string.Concat("  Source (.codex)\n", string.Concat("    |-> Lexer      tokenizes source text\n", string.Concat("    |-> Parser     builds syntax tree\n", string.Concat("    |-> Desugarer  expands syntactic sugar\n", string.Concat("    |-> NameResolver  resolves identifiers to bindings\n", string.Concat("    |-> TypeChecker   Hindley-Milner inference + effect tracking\n", string.Concat("    |-> Lowering      AST -> IR (IRModule, IRNode)\n", string.Concat("    |-> Emitter       IR -> target code\n", string.Concat("\n", string.Concat("  Hot path files (self-hosted):\n", string.Concat("    Codex.Codex/Syntax/Lexer.codex\n", string.Concat("    Codex.Codex/Syntax/Parser.codex\n", string.Concat("    Codex.Codex/Types/TypeChecker.codex\n", string.Concat("    Codex.Codex/Types/Unifier.codex\n", string.Concat("    Codex.Codex/IR/Lowering.codex\n", string.Concat("    Codex.Codex/Emit/CSharpEmitter.codex\n", "=========================")))))))))))))))))) : Console.WriteLine(string.Concat("  Unknown topic: ", string.Concat(topic, ". Available: syntax, pipeline, agents, style, git, tools, structure, pitfalls")))))))))));
        return null;
    }

    public static object do_orient(List<string> args)
    {
        ((Func<long, object>)((argc) => ((argc < 3L) ? orient_level0() : orient_topic(args[(int)2L]))))(((long)args.Count));
        return null;
    }

    public static object do_init(List<string> args)
    {
        ((Func<string, object>)((agent) => ((Func<string, object>)((doctor_out) => ((Func<string, object>)((check_out) => ((Func<string, object>)((status_out) => ((Func<string, object>)((handoff_out) => ((Func<string, object>)((verify_out) => ((Func<string, object>)((git_log) => ((Func<string, object>)((git_branches) => Console.WriteLine(string.Concat(doctor_out, string.Concat("\n", string.Concat(check_out, string.Concat("\n", string.Concat(status_out, string.Concat("\n", string.Concat(handoff_out, string.Concat("\n", string.Concat(verify_out, string.Concat("\n=== Recent Commits ===\n", string.Concat(git_log, string.Concat("\n=== Remote Branches ===\n", git_branches)))))))))))))))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("git", "branch -r") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("git", "log --oneline -5") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("pwsh", "-File tools/codex-agent-verify.ps1 docs/CurrentPlan.md docs/ToDo/CSharpCleanup.md docs/TOOL-ERROR-REGISTRY.md docs/KNOWN-CONDITIONS.md .github/copilot-instructions.md") { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("dotnet", string.Concat(agent, " handoff show")) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("dotnet", string.Concat(agent, " status")) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("dotnet", string.Concat(agent, " check")) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo("dotnet", string.Concat(agent, " doctor")) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())))("tools/codex-agent/codex-agent.dll");
        return null;
    }

    public static object main()
    {
        ((Func<object>)(() => {
                ((Func<List<string>, object>)((args) => ((Func<long, object>)((argc) => ((argc < 2L) ? print_help() : ((Func<string, object>)((cmd) => ((cmd == "peek") ? do_peek(args) : ((cmd == "stat") ? do_stat(args) : ((cmd == "snap") ? do_snap(args) : ((cmd == "init") ? do_init(args) : ((cmd == "status") ? do_status(args) : ((cmd == "plan") ? do_plan(args) : ((cmd == "check") ? do_check(args) : ((cmd == "doctor") ? do_doctor(args) : ((cmd == "handoff") ? do_handoff(args) : ((cmd == "log") ? do_log(args) : ((cmd == "recall") ? do_recall(args) : ((cmd == "build") ? do_build(args) : ((cmd == "test") ? do_test(args) : ((cmd == "orient") ? do_orient(args) : ((cmd == "greet") ? do_greet(args) : ((cmd == "roster") ? do_roster(args) : ((cmd == "help") ? print_help() : Console.WriteLine(string.Concat("Unknown command: ", string.Concat(cmd, ". Run 'codex-agent help' for usage."))))))))))))))))))))))(args[(int)1L]))))(((long)args.Count))))(new List<string>(Environment.GetCommandLineArgs()));
                return null;
            }))();
        return null;
    }

}

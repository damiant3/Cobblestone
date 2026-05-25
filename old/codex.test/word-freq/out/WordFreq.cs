using System;
using System.Collections.Generic;
using System.Linq;

Codex_WordFreq.main();

public abstract record LookupResult;

public sealed record Found(long Field0) : LookupResult;
public sealed record NotFound : LookupResult;

public sealed record WordCount(string word, long count);

public sealed record FreqMap(List<WordCount> entries, long total_words);

public static class Codex_WordFreq
{
    public static FreqMap freq_empty()
    {
        return new FreqMap(new List<WordCount>(), 0L);
    }

    public static LookupResult freq_lookup(List<WordCount> entries, string word)
    {
        return freq_lookup_loop(entries, word, 0L, ((long)entries.Count));
    }

    public static LookupResult freq_lookup_loop(List<WordCount> entries, string word, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new NotFound();
            }
            else
            {
                var e = entries[(int)i];
                if ((e.word == word))
                {
                    return new Found(e.count);
                }
                else
                {
                    var _tco_0 = entries;
                    var _tco_1 = word;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    entries = _tco_0;
                    word = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    continue;
                }
            }
        }
    }

    public static List<WordCount> freq_update(List<WordCount> entries, string word)
    {
        return freq_update_loop(entries, word, 0L, ((long)entries.Count), new List<WordCount>());
    }

    public static List<WordCount> freq_update_loop(List<WordCount> entries, string word, long i, long len, List<WordCount> acc)
    {
        while (true)
        {
            if ((i == len))
            {
                return Enumerable.Concat(acc, new List<WordCount>() { new WordCount(word, 1L) }).ToList();
            }
            else
            {
                var e = entries[(int)i];
                if ((e.word == word))
                {
                    return Enumerable.Concat(acc, Enumerable.Concat(new List<WordCount>() { new WordCount(word, (e.count + 1L)) }, rest_of_list(entries, (i + 1L), len)).ToList()).ToList();
                }
                else
                {
                    var _tco_0 = entries;
                    var _tco_1 = word;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    var _tco_4 = Enumerable.Concat(acc, new List<WordCount>() { e }).ToList();
                    entries = _tco_0;
                    word = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    acc = _tco_4;
                    continue;
                }
            }
        }
    }

    public static List<WordCount> rest_of_list(List<WordCount> entries, long i, long len)
    {
        return ((i == len) ? new List<WordCount>() : Enumerable.Concat(new List<WordCount>() { entries[(int)i] }, rest_of_list(entries, (i + 1L), len)).ToList());
    }

    public static FreqMap freq_add_word(FreqMap fm, string word)
    {
        return new FreqMap(freq_update(fm.entries, word), (fm.total_words + 1L));
    }

    public static FreqMap count_words(List<string> words)
    {
        return count_words_loop(words, 0L, ((long)words.Count), freq_empty());
    }

    public static FreqMap count_words_loop(List<string> words, long i, long len, FreqMap fm)
    {
        while (true)
        {
            if ((i == len))
            {
                return fm;
            }
            else
            {
                var _tco_0 = words;
                var _tco_1 = (i + 1L);
                var _tco_2 = len;
                var _tco_3 = freq_add_word(fm, words[(int)i]);
                words = _tco_0;
                i = _tco_1;
                len = _tco_2;
                fm = _tco_3;
                continue;
            }
        }
    }

    public static long freq_size(FreqMap fm)
    {
        return ((long)fm.entries.Count);
    }

    public static bool is_word_char(string c)
    {
        return ((c.Length > 0 && char.IsLetter(c[0])) || (c.Length > 0 && char.IsDigit(c[0])));
    }

    public static string to_lower_char(string c)
    {
        return ((Func<long, string>)((code) => (((code >= 65L) && (code <= 90L)) ? ((char)(code + 32L)).ToString() : c)))(((long)c[0]));
    }

    public static string normalize_word(string word, long i, long len, string acc)
    {
        while (true)
        {
            if ((i == len))
            {
                return acc;
            }
            else
            {
                var _tco_0 = word;
                var _tco_1 = (i + 1L);
                var _tco_2 = len;
                var _tco_3 = string.Concat(acc, to_lower_char(word[(int)i].ToString()));
                word = _tco_0;
                i = _tco_1;
                len = _tco_2;
                acc = _tco_3;
                continue;
            }
        }
    }

    public static string extract_word(string input, long start, long pos, long len)
    {
        while (true)
        {
            if ((pos == len))
            {
                return input.Substring((int)start, (int)(pos - start));
            }
            else
            {
                if (is_word_char(input[(int)pos].ToString()))
                {
                    var _tco_0 = input;
                    var _tco_1 = start;
                    var _tco_2 = (pos + 1L);
                    var _tco_3 = len;
                    input = _tco_0;
                    start = _tco_1;
                    pos = _tco_2;
                    len = _tco_3;
                    continue;
                }
                else
                {
                    return input.Substring((int)start, (int)(pos - start));
                }
            }
        }
    }

    public static long skip_non_word(string input, long pos, long len)
    {
        while (true)
        {
            if ((pos == len))
            {
                return pos;
            }
            else
            {
                if (is_word_char(input[(int)pos].ToString()))
                {
                    return pos;
                }
                else
                {
                    var _tco_0 = input;
                    var _tco_1 = (pos + 1L);
                    var _tco_2 = len;
                    input = _tco_0;
                    pos = _tco_1;
                    len = _tco_2;
                    continue;
                }
            }
        }
    }

    public static List<string> tokenize_loop(string input, long pos, long len, List<string> acc)
    {
        while (true)
        {
            if ((pos >= len))
            {
                return acc;
            }
            else
            {
                var start = skip_non_word(input, pos, len);
                if ((start >= len))
                {
                    return acc;
                }
                else
                {
                    var raw = extract_word(input, start, start, len);
                    var word = normalize_word(raw, 0L, ((long)raw.Length), "");
                    if ((((long)word.Length) > 0L))
                    {
                        var _tco_0 = input;
                        var _tco_1 = (start + ((long)raw.Length));
                        var _tco_2 = len;
                        var _tco_3 = Enumerable.Concat(acc, new List<string>() { word }).ToList();
                        input = _tco_0;
                        pos = _tco_1;
                        len = _tco_2;
                        acc = _tco_3;
                        continue;
                    }
                    else
                    {
                        var _tco_0 = input;
                        var _tco_1 = (start + 1L);
                        var _tco_2 = len;
                        var _tco_3 = acc;
                        input = _tco_0;
                        pos = _tco_1;
                        len = _tco_2;
                        acc = _tco_3;
                        continue;
                    }
                }
            }
        }
    }

    public static List<string> tokenize_words(string input)
    {
        return tokenize_loop(input, 0L, ((long)input.Length), new List<string>());
    }

    public static string sample_text()
    {
        return "The quick brown fox jumps over the lazy dog. The fox was quick and the dog was lazy. A fox is a fox.";
    }

    public static string format_entry(WordCount wc)
    {
        return string.Concat("  ", string.Concat(wc.word, string.Concat(": ", Convert.ToString(wc.count))));
    }

    public static string format_entries(List<WordCount> entries, long i, long len, string acc)
    {
        while (true)
        {
            if ((i == len))
            {
                return acc;
            }
            else
            {
                var line = format_entry(entries[(int)i]);
                var sep = ((i == 0L) ? "" : ((char)10L).ToString());
                var _tco_0 = entries;
                var _tco_1 = (i + 1L);
                var _tco_2 = len;
                var _tco_3 = string.Concat(acc, string.Concat(sep, line));
                entries = _tco_0;
                i = _tco_1;
                len = _tco_2;
                acc = _tco_3;
                continue;
            }
        }
    }

    public static string show_lookup(LookupResult r)
    {
        return ((Func<LookupResult, string>)((_scrutinee0_) => (_scrutinee0_ is Found _mFound0_ ? ((Func<long, string>)((n) => Convert.ToString(n)))((long)_mFound0_.Field0) : (_scrutinee0_ is NotFound _mNotFound0_ ? "not found" : throw new InvalidOperationException("Non-exhaustive match")))))(r);
    }

    public static WordCount find_max_entry(List<WordCount> entries, long i, long len, WordCount best)
    {
        while (true)
        {
            if ((i == len))
            {
                return best;
            }
            else
            {
                var e = entries[(int)i];
                if ((e.count > best.count))
                {
                    var _tco_0 = entries;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    var _tco_3 = e;
                    entries = _tco_0;
                    i = _tco_1;
                    len = _tco_2;
                    best = _tco_3;
                    continue;
                }
                else
                {
                    var _tco_0 = entries;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    var _tco_3 = best;
                    entries = _tco_0;
                    i = _tco_1;
                    len = _tco_2;
                    best = _tco_3;
                    continue;
                }
            }
        }
    }

    public static object main()
    {
        ((Func<List<string>, object>)((words) => ((Func<FreqMap, object>)((fm) => ((Func<string, object>)((input_line) => ((Func<string, object>)((results) => ((Func<string, object>)((the_count) => ((Func<string, object>)((fox_count) => ((Func<WordCount, object>)((most) => ((Func<string, object>)((most_line) => ((Func<object>)(() => {
                Console.WriteLine("Word Frequency Analysis");
                Console.WriteLine("=======================");
                Console.WriteLine(input_line);
                Console.WriteLine("");
                Console.WriteLine("Frequencies:");
                Console.WriteLine(results);
                Console.WriteLine("");
                Console.WriteLine(the_count);
                Console.WriteLine(fox_count);
                Console.WriteLine(most_line);
                return null;
            }))()))(string.Concat("Most frequent: '", string.Concat(most.word, string.Concat("' (", string.Concat(Convert.ToString(most.count), " times)")))))))(((freq_size(fm) > 0L) ? find_max_entry(fm.entries, 1L, ((long)fm.entries.Count), fm.entries[(int)0L]) : new WordCount("", 0L)))))(string.Concat("Count for 'fox': ", show_lookup(freq_lookup(fm.entries, "fox"))))))(string.Concat("Count for 'the': ", show_lookup(freq_lookup(fm.entries, "the"))))))(format_entries(fm.entries, 0L, ((long)fm.entries.Count), ""))))(string.Concat("Input: ", string.Concat(Convert.ToString(((long)sample_text().Length)), string.Concat(" chars, ", string.Concat(Convert.ToString(fm.total_words), string.Concat(" words, ", string.Concat(Convert.ToString(freq_size(fm)), " unique")))))))))(count_words(words))))(tokenize_words(sample_text()));
        return null;
    }

}

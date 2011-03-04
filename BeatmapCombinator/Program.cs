﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using osum.GameplayElements.Beatmaps;
using osum.GameplayElements;

namespace BeatmapCombinator
{
    class BeatmapDifficulty : Beatmap
    {
        internal string VersionName;
        internal List<HitObjectLine> HitObjectLines = new List<HitObjectLine>();
        internal List<string> HeaderLines = new List<string>();

        internal double VelocityAt(int time)
        {
            return (100000.0f * DifficultySliderMultiplier / beatLengthAt(time, true));
        }

        internal double ScoringDistanceAt(int time)
        {
            return ((100 * DifficultySliderMultiplier / bpmMultiplierAt(time)) / DifficultySliderTickRate);
        }
    }

    class HitObjectLine
    {
        internal string StringRepresentation;
        internal int Time;
    }

    class BeatmapCombinator
    {
        /// <summary>
        /// Combines many .osu files into one .osc
        /// </summary>
        /// <param name="args">Directory containing many .osu files</param>
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("No path specified!");
                return;
            }

            List<string> osuFiles = new List<string>(Directory.GetFiles(args[0], "*.osu"));

            if (osuFiles.Count < 1)
            {
                Console.WriteLine("No .osu files found!");
                return;
            }

            string newFilename = osuFiles[0].Remove(osuFiles[0].LastIndexOf('[') - 1) + ".osc";

            List<string> orderedDifficulties = new List<string>();

            orderedDifficulties.Add(osuFiles.Find(f => f.EndsWith("[Easy].osu")));
            orderedDifficulties.Add(osuFiles.Find(f => f.EndsWith("[Normal].osu")));
            orderedDifficulties.Add(osuFiles.Find(f => f.EndsWith("[Hard].osu")));
            orderedDifficulties.Add(osuFiles.Find(f => f.EndsWith("[Insane].osu")));

            Console.WriteLine("Files found:");
            Console.WriteLine(string.Join("\n", orderedDifficulties));

            List<BeatmapDifficulty> difficulties = new List<BeatmapDifficulty>();

            foreach (string f in orderedDifficulties)
            {
                if (f == null) continue;

                BeatmapDifficulty bd = new BeatmapDifficulty();
                difficulties.Add(bd);

                string currentSection = "";

                foreach (string line in File.ReadAllLines(f))
                {
                    if (line.StartsWith("Version:"))
                    {
                        bd.VersionName = line.Replace("Version:", "");
                        continue;
                    }

                    if (line.StartsWith("["))
                        currentSection = line.Replace("[", "").Replace("]", "");
                    else if (line.Length > 0)
                    {
                        string[] split = line.Split(',');
                        string[] var = line.Split(':');
                        string key = string.Empty;
                        string val = string.Empty;
                        if (var.Length > 1)
                        {
                            key = var[0].Trim();
                            val = var[1].Trim();
                        }
                        
                        switch (currentSection)
                        {
                            case "Difficulty":
                                switch (key)
                                {
                                    case "HPDrainRate":
                                        bd.DifficultyHpDrainRate = Math.Min((byte)10, Math.Max((byte)0, byte.Parse(val)));
                                        break;
                                    case "CircleSize":
                                        bd.DifficultyCircleSize = Math.Min((byte)10, Math.Max((byte)0, byte.Parse(val)));
                                        break;
                                    case "OverallDifficulty":
                                        bd.DifficultyOverall = Math.Min((byte)10, Math.Max((byte)0, byte.Parse(val)));
                                        //if (!hasApproachRate) DifficultyApproachRate = DifficultyOverall;
                                        break;
                                    case "SliderMultiplier":
                                        bd.DifficultySliderMultiplier =
                                            Math.Max(0.4, Math.Min(3.6, Double.Parse(val)));
                                        break;
                                    case "SliderTickRate":
                                        bd.DifficultySliderTickRate =
                                            Math.Max(0.5, Math.Min(8, Double.Parse(val)));
                                        break;
                                }
                                break;
                            case "HitObjects":
                                HitObjectType type = (HitObjectType)(Int32.Parse(split[3]) & 15);
                                int time = (int)Decimal.Parse(split[2]);

                                string stringRep = (int)bd.controlPointAt(time).sampleSet + "," + line;

                                //add addition difficulty-specific information
                                if ((type & HitObjectType.Slider) > 0)
                                {
                                    if (split.Length < 9) stringRep += ",";

                                    //velocity and scoring distance.
                                    stringRep += "," + bd.VelocityAt(time) + "," + bd.ScoringDistanceAt(time);

                                    
                                }

                                bd.HitObjectLines.Add(new HitObjectLine() { StringRepresentation = stringRep, Time = Int32.Parse(line.Split(',')[2]) });
                                continue; //skip direct output
                            case "TimingPoints":
                                ControlPoint cp = new ControlPoint(Double.Parse(split[0]),
                                                             Double.Parse(split[1]),
                                                             split[2][0] == '0' ? TimeSignatures.SimpleQuadruple :
                                                             (TimeSignatures)Int32.Parse(split[2]),
                                                             (SampleSet)Int32.Parse(split[3]),
                                                             split.Length > 4
                                                                 ? (CustomSampleSet)Int32.Parse(split[4])
                                                                 : CustomSampleSet.Default,
                                                             Int32.Parse(split[5]),
                                                             split.Length > 6 ? split[6][0] == '1' : true,
                                                             split.Length > 7 ? split[7][0] == '1' : false);
                                bd.ControlPoints.Add(cp);
                                break;
                        }
                    }

                    bd.HeaderLines.Add(line);
                }
            }

            using (StreamWriter output = new StreamWriter(newFilename))
            {
                //write headers first (use first difficulty as arbitrary source)
                foreach (string l in difficulties[0].HeaderLines)
                    output.WriteLine(l);

                //keep track of how many hitObject lines are remaining for each difficulty
                int[] linesRemaining = new int[difficulties.Count];
                for (int i = 0; i < difficulties.Count; i++)
                {
                    linesRemaining[i] = difficulties[i] == null ? 0 : difficulties[i].HitObjectLines.Count;
                }

                int currentTime = 0;

                while (!linesRemaining.All(i => i == 0))
                {
                    int bestMatchDifficulty = -1;
                    HitObjectLine bestMatchLine = null;

                    for (int i = 0; i < difficulties.Count; i++)
                    {
                        if (linesRemaining[i] == 0)
                            continue;

                        int holOffset = difficulties[i].HitObjectLines.Count - linesRemaining[i];
                        
                        HitObjectLine line = difficulties[i].HitObjectLines[holOffset];

                        if (line.Time > currentTime && (bestMatchLine == null || line.Time < bestMatchLine.Time))
                        {
                            bestMatchDifficulty = i;
                            bestMatchLine = line;
                        }
                    }

                    output.WriteLine(bestMatchDifficulty + "," + bestMatchLine.StringRepresentation);
                    
                    linesRemaining[bestMatchDifficulty]--;
                }
            }
        }
    }
}
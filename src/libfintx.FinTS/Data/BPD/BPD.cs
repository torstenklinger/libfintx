﻿/*	
 * 	
 *  This file is part of libfintx.
 *  
 *  Copyright (C) 2020 Abid Hussain
 *  
 *  This program is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU Lesser General Public
 *  License as published by the Free Software Foundation; either
 *  version 3 of the License, or (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 *  Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with this program; if not, write to the Free Software Foundation,
 *  Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 * 	
 */

using libfintx.FinTS.Data.Segment;
using libfintx.FinTS.Util;
using libfintx.Logger.Log;
using System;
using System.Collections.Generic;
using System.Linq;

namespace libfintx.FinTS
{
    public static class BPD
    {
        public static string Value { get; set; }

        public static HIPINS HIPINS { get; set; }

        public static List<HICAZS> HICAZS { get; set; }

        public static List<HIKAZS> HIKAZS { get; set; }

        public static List<Segment> SegmentList { get; set; }

        public static void Reset()
        {
            ReflectionUtil.ResetStaticFields(typeof(BPD));
        }

        public static void ParseBpd(string bpd)
        {
            Value = bpd;
            SegmentList = new List<Segment>();
            HICAZS = new List<HICAZS>();
            HIKAZS = new List<HIKAZS>();

            List<string> segments = Helper.SplitSegments(bpd);
            foreach (var rawSegment in segments)
            {
                try
                {
                    var segment = SegmentParserFactory.ParseSegment(rawSegment);
                    if (segment is HIPINS)
                        HIPINS = (HIPINS) segment;
                    else if (segment is HICAZS)
                        HICAZS.Add((HICAZS) segment);
                    else if (segment is HIKAZS)
                        HIKAZS.Add((HIKAZS) segment);

                    SegmentList.Add(segment);
                }
                catch (Exception ex)
                {
                    Log.Write($"Couldn't parse segment: {ex.Message}{Environment.NewLine}{rawSegment}");
                }
            }
        }
    }
}

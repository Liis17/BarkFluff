INCLUDE PERFETTO MODULE android.power_rails;

SELECT
  power_rail_name AS rail,
  SUM(
    energy_delta *
    (MIN(ts + dur, {{END_NS}}) - MAX(ts, {{START_NS}})) / dur
  ) AS energy_uws
FROM android_power_rails_counters
WHERE dur > 0
  AND ts < {{END_NS}}
  AND ts + dur > {{START_NS}}
GROUP BY power_rail_name
ORDER BY power_rail_name;

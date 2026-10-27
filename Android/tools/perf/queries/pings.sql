SELECT
  SUM(CASE WHEN dur >= 0 THEN 1 ELSE 0 END) AS completed_count,
  AVG(CASE WHEN dur >= 0 THEN CAST(dur AS DOUBLE) / 1000000 END) AS mean_duration_ms,
  SUM(CASE WHEN dur = -1 THEN 1 ELSE 0 END) AS unfinished_count
FROM slice
WHERE name = 'BF:onlinePing'
  AND ts >= {{START_NS}}
  AND ts < {{END_NS}};

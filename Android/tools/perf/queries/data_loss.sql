SELECT name, idx, severity, source, value
FROM stats
WHERE (severity IN ('data_loss', 'error') AND value != 0);

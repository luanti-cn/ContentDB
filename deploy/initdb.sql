SELECT 'CREATE DATABASE contentdb OWNER contentdb'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'contentdb')\gexec

SELECT 'CREATE DATABASE contentdb_mirror OWNER contentdb'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'contentdb_mirror')\gexec
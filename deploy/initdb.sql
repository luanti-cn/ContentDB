-- 首次初始化时自动创建第二个数据库(MirrorDb)。
-- contentdb 主库由 POSTGRES_DB 环境变量自动创建。
CREATE DATABASE contentdb_mirror OWNER contentdb;

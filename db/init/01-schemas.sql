-- Runs once, on first start of the local postgres:17 compose service
-- (docker-entrypoint-initdb.d). Creates both schemas and the extension the
-- post schema needs. The post schema's tables come from EF Core migrations;
-- the search schema's tables come from 02-search-schema.sql, which compose
-- mounts next to this file.
create extension if not exists citext;
create schema if not exists post;
create schema if not exists search;

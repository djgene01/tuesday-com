-- Performance fixtures: clones the Campaign calendar board's structure at scale.
--
--   docker exec -i tuesday-postgres psql -U tuesday -d tuesday \
--     -v name="'Perf 5k'" -v count=5000 < scripts/perf/seed.sql
--
-- Re-running with the same name replaces the fixture board.

\set ON_ERROR_STOP on

create temp table src  as
select b."Id", b."TenantId", b."WorkspaceId", b."OwnerId"
from boards b
where b."Name" = 'Campaign calendar'
limit 1;

delete from item_column_values
where "ItemId" in (select i."Id" from items i join boards b on b."Id" = i."BoardId" where b."Name" = :'name');

delete from items where "BoardId" in (select "Id" from boards where "Name" = :'name');
delete from board_columns where "BoardId" in (select "Id" from boards where "Name" = :'name');
delete from item_groups where "BoardId" in (select "Id" from boards where "Name" = :'name');
delete from boards where "Name" = :'name';

insert into boards ("Id", "TenantId", "WorkspaceId", "Name", "Type", "OwnerId", "CreatedAt")
select gen_random_uuid(), src."TenantId", src."WorkspaceId", :'name', 'Main', src."OwnerId", now()
from src;

create temp table nboard  as
select "Id", "TenantId" from boards where "Name" = :'name';

insert into item_groups ("Id", "TenantId", "BoardId", "Title", "Colour", "Position")
select gen_random_uuid(), n."TenantId", n."Id", 'Group ' || s,
       (array['#6161FF', '#00C875', '#FDAB3D'])[s], s - 1
from nboard n, generate_series(1, 3) as s;

insert into board_columns ("Id", "TenantId", "BoardId", "Key", "Title", "Type", "Position", "SettingsJson")
select gen_random_uuid(), src."TenantId", n."Id", c."Key", c."Title", c."Type", c."Position", c."SettingsJson"
from src
join board_columns c on c."BoardId" = src."Id"
cross join nboard n;

-- Items are spread across the three groups so the grid exercises group bands too.
insert into items ("Id", "TenantId", "BoardId", "GroupId", "Name", "Position", "CreatedAt", "ParentItemId")
select gen_random_uuid(), n."TenantId", n."Id", g."Id",
       'Perf item ' || lpad(i::text, 6, '0'),
       (i - 1) / 3,
       now() - (i || ' seconds')::interval,
       null
from nboard n
join generate_series(1, :count) as i on true
join (select "Id", row_number() over (order by "Position") as rn from item_groups where "BoardId" = (select "Id" from nboard)) g
     on g.rn = (i % 3) + 1;

-- Two cells per item: enough for the grid to render real values and stay realistic
-- without turning the fixture into a bulk-write benchmark.
insert into item_column_values ("Id", "TenantId", "ItemId", "ColumnId", "TextValue", "UpdatedAt")
select gen_random_uuid(), i."TenantId", i."Id", c."Id",
       (array['Not started', 'Working on it', 'Stuck', 'Done'])[(i."Position" % 4) + 1],
       now()
from items i
join board_columns c on c."BoardId" = i."BoardId" and c."Type" = 'Status'
where i."BoardId" = (select "Id" from nboard);

-- A board with no view renders an empty state and the "measurement" then times a 15 KB page.
-- Copy the source board's views so the fixture actually draws the 5,000-row grid.
insert into board_views ("Id", "TenantId", "BoardId", "Name", "Type", "Position", "SettingsJson")
select gen_random_uuid(), n."TenantId", n."Id", v."Name", v."Type", v."Position", v."SettingsJson"
from nboard n
join board_views v on v."BoardId" = (select "Id" from src) and v."Position" = 0;

select 'seeded ' || :'name' || ' with ' || count(*) || ' items and ' ||
       (select count(*) from item_column_values v join items i on i."Id" = v."ItemId"
         where i."BoardId" = (select "Id" from nboard)) || ' cells' as result
from items where "BoardId" = (select "Id" from nboard);

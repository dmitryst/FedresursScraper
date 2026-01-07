-- выбираем торги у которых минимум два лота 
select 
	b."Id" as "BiddingId", 
	array_agg(l."Id") as "LotIds", 
	array_agg(l."Description") as "LotDescriptions"
	--string_agg(l."Description", '; ') as "LotDescriptions"  -- если нужно в одну строку
from "Biddings" b
join "Lots" l on l."BiddingId" = b."Id"
where b."TradeNumber" like '%ОАОФ'
group by b."Id"
having count(l."Id") >= 2
order by b."CreatedAt" 
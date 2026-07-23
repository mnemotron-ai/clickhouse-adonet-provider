SELECT CAST(number % 3 AS Enum8('lo' = 0, 'mid' = 1, 'hi' = 2)) AS e8, CAST(number % 2 AS Enum16('a' = 0, 'b' = 1)) AS e16 FROM numbers(7) ORDER BY number

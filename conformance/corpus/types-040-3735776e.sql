SELECT toFixedString(concat('fs', toString(number)), 8) AS f, toString(number * 100) AS s FROM numbers(7) ORDER BY number

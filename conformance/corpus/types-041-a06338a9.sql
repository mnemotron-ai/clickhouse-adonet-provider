SELECT toDecimal32(number * 0.1, 3) AS d32, toDecimal64(number * 1.5 - 3, 4) AS d64, toDecimal128(number * 1000000.000001 - 2000000, 10) AS d128 FROM numbers(7) ORDER BY number

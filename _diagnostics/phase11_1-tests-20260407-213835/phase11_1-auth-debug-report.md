# Phase 11.1 Auth Debug Verification Report

Generated: 2026-04-07 21:38:35
BaseUrl: http://localhost:5091

## Public diagnostics endpoint works
- Result: PASS
- Detail: Issuer=VeritasAtlas, Audience=VeritasAtlasUsers

## Invalid login rejected
- Result: PASS
- Detail: Le serveur distant a retourné une erreur : (401) Non autorisé.

## Valid login accepted
- Result: PASS
- Detail: OK

## Auth me works
- Result: FAIL
- Detail: Le serveur distant a retourné une erreur : (401) Non autorisé.

## Protected diagnostics works
- Result: FAIL
- Detail: Le serveur distant a retourné une erreur : (401) Non autorisé.

## Protected workflow accepts token
- Result: FAIL
- Detail: Le serveur distant a retourné une erreur : (401) Non autorisé.

## Protected audit accepts token
- Result: FAIL
- Detail: Le serveur distant a retourné une erreur : (401) Non autorisé.


# Phase 12 Verification Report

Generated: 2026-04-07 23:39:39
BaseUrl: http://localhost:5091

## Login works
- Result: PASS
- Detail: OK

## Auth me works
- Result: PASS
- Detail:  / admin

## Seed lifecycle works
- Result: PASS
- Detail: OK

## Snapshot exists after seed
- Result: PASS
- Detail: Exists: True

## Prepare publication works
- Result: FAIL
- Detail: Le serveur distant a retourné une erreur : (400) Demande incorrecte.

## Resolve contradiction works
- Result: PASS
- Detail: OK

## Snapshot updates after transitions
- Result: PASS
- Detail: CaseStatus=Open, ContradictionStatus=Resolved

## Reset works
- Result: PASS
- Detail: OK

## Snapshot cleared after reset
- Result: PASS
- Detail: Exists: False

## Audit endpoint accessible
- Result: PASS
- Detail: OK


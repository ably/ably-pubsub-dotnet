# Given/When/Then prose tests (catalogue C-14; decision log #44): hand-review list

30 tests @0e52e656 whose `pseudo` fences hold Given/When/Then/And prose instead of pseudocode (148 lines).
Translate per C-14 and list each in the final report for hand review (the audit sees no spec-side ASSERT).
The 28 `batch_publish.md` tests are also pending stubs (RSC22 `batchPublish` is missing in this SDK; rest notes section 10).

## uts/rest/unit/batch_publish.md (28)
- rest/unit/RSC22c/single-spec-post-messages-0
- rest/unit/RSC22c/array-specs-post-messages-0
- rest/unit/RSC22c/single-spec-single-result-0
- rest/unit/RSC22c/array-specs-array-results-0
- rest/unit/RSC22c/multiple-channels-multiple-results-0
- rest/unit/RSC22c/messages-encoded-per-rsl4-0
- rest/unit/RSC22c/uses-configured-auth-0
- rest/unit/RSC22d/idempotent-ids-generated-0
- rest/unit/RSC22d/explicit-ids-preserved-0
- rest/unit/RSC22d/ids-not-generated-disabled-0
- rest/unit/BSP2a/channels-array-strings-0
- rest/unit/BSP2b/messages-array-objects-0
- rest/unit/BPR2a/success-channel-name-0
- rest/unit/BPR2b/success-message-id-prefix-0
- rest/unit/BPR2c/serials-array-0
- rest/unit/BPR2c/serials-null-conflated-0
- rest/unit/BPF2a/failure-channel-name-0
- rest/unit/BPF2b/failure-error-info-0
- rest/unit/RSC22c/partial-success-mixed-results-0
- rest/unit/RSC22c/distinguish-success-failure-0
- rest/unit/RSC22/empty-channels-rejected-0
- rest/unit/RSC22/empty-messages-rejected-0
- rest/unit/RSC22/server-error-propagated-0
- rest/unit/RSC22/auth-error-propagated-0
- rest/unit/RSC22/standard-headers-included-0
- rest/unit/RSC22/request-id-included-0
- rest/unit/RSC22/multiple-messages-per-channel-0
- rest/unit/RSC22/multiple-channels-multiple-messages-0

## uts/rest/unit/presence/rest_presence.md (2)
- rest/unit/RSP1a/presence-channel-attribute-0
- rest/unit/RSP1b/same-instance-returned-0

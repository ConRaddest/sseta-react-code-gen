// !!---------------------------------------------------!!
// !!---------- AUTO-GENERATED: Do not edit! -----------!!
// !!---------------------------------------------------!!

"use client"

import { useState, useEffect } from "react"
import { SubmitHandler, useForm } from "react-hook-form"
import { Button, FormTemplate, FormValidationErrors, FilterBy, OrderBy, extractApiErrors } from "@sseta/components"
import { useAccessStaffRoleRequest } from "@/contexts/resources/access/AccessStaffRoleRequestContext"
import { useToast } from "@/contexts/general/ToastContext"
import { AccessStaffRoleRequestCreateRequest } from "@/types/api.types"
import useAccessStaffRoleRequestCreateFields from "./useAccessStaffRoleRequestCreateFields"

interface AccessStaffRoleRequestCreateFormProps {
  defaultValues?: Partial<AccessStaffRoleRequestCreateRequest>
  disabledFields?: string[]
  hiddenFields?: string[]
  selectFilterBys?: Record<string, FilterBy[]>
  selectOrderBys?: Record<string, OrderBy[]>
  renderActionsInFooter?: boolean
  className?: string
  loading?: boolean
  onCreated?: () => void
}

export default function AccessStaffRoleRequestCreateForm(props: AccessStaffRoleRequestCreateFormProps) {
  const {
    defaultValues,
    disabledFields,
    hiddenFields,
    selectFilterBys = {},
    selectOrderBys = {},
    renderActionsInFooter = true,
    className = "px-6 py-4",
    loading: loadingOverride,
    onCreated,
  } = props

  const [apiErrors, setApiErrors] = useState<string[]>([])
  const [loading, setLoading] = useState(false)
  const isLoading = loadingOverride ?? loading

  const { create } = useAccessStaffRoleRequest()
  const { showToast } = useToast()

  const {
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<AccessStaffRoleRequestCreateRequest>({
    mode: "onBlur",
  })

  const { fields, layout } = useAccessStaffRoleRequestCreateFields({ errors, disabledFields, selectFilterBys, selectOrderBys })

  useEffect(() => {
    if (defaultValues && Object.keys(defaultValues).length > 0) {
      reset((formValues) => ({ ...formValues, ...defaultValues }))
    }
  }, [])

  const onSubmit: SubmitHandler<AccessStaffRoleRequestCreateRequest> = async (data) => {
    setLoading(true)
    setApiErrors([])
    try {
      const result = await create(data)
      showToast("Staff Role Request successfully created", "success")
      onCreated?.()
    } catch (error: any) {
      setApiErrors(extractApiErrors(error))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex flex-col flex-1 min-h-0">
      <FormValidationErrors errors={apiErrors} className="mx-auto max-w-4xl w-full mb-4" />
      <FormTemplate
        control={control}
        fields={fields}
        layout={layout}
        hiddenFields={hiddenFields}
        renderActionsInFooter={renderActionsInFooter}
        isLoading={isSubmitting || isLoading}
        onSubmit={handleSubmit(onSubmit)}
        className={className}
        actions={
          <div className="flex md:flex-row flex-col gap-2">
            <Button loading={isSubmitting} type="submit" variant="orange" size="mlg" className="w-full md:w-40">
              Save
            </Button>
          </div>
        }
      />
    </div>
  )
}

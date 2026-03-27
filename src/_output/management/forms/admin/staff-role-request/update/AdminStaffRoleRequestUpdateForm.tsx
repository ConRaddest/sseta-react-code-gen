// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

"use client"

import { useState, useEffect } from "react"
import { SubmitHandler, useForm } from "react-hook-form"
import { Button, FormTemplate, FormValidationErrors, FilterBy, OrderBy, extractApiErrors } from "@sseta/components"
import { useAdminStaffRoleRequest } from "@/contexts/resources/admin/AdminStaffRoleRequestContext"
import { useToast } from "@/contexts/general/ToastContext"
import { AdminStaffRoleRequestUpdateRequest } from "@/types/api.types"
import useAdminStaffRoleRequestUpdateFields from "./useAdminStaffRoleRequestUpdateFields"

interface AdminStaffRoleRequestUpdateFormProps {
  staffRoleRequestId: number
  defaultValues?: Partial<AdminStaffRoleRequestUpdateRequest>
  disabledFields?: string[]
  hiddenFields?: string[]
  selectFilterBys?: Record<string, FilterBy[]>
  selectOrderBys?: Record<string, OrderBy[]>
  renderActionsInFooter?: boolean
  className?: string
  loading?: boolean
  onUpdated?: (staffRoleRequestId: number) => void
}

export default function AdminStaffRoleRequestUpdateForm(props: AdminStaffRoleRequestUpdateFormProps) {
  const {
    staffRoleRequestId,
    defaultValues,
    disabledFields,
    hiddenFields,
    selectFilterBys = {},
    selectOrderBys = {},
    renderActionsInFooter = true,
    className = "px-6 py-4",
    loading: loadingOverride,
    onUpdated,
  } = props

  const [apiErrors, setApiErrors] = useState<string[]>([])
  const [loading, setLoading] = useState(false)
  const isLoading = loadingOverride ?? loading

  const { retrieve, update } = useAdminStaffRoleRequest()
  const { showToast } = useToast()

  const {
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<AdminStaffRoleRequestUpdateRequest>({
    mode: "onBlur",
  })

  const { fields, layout } = useAdminStaffRoleRequestUpdateFields({ errors, disabledFields, selectFilterBys, selectOrderBys })

  useEffect(() => {
    const fetchRecord = async () => {
      setLoading(true)
      try {
        const record = await retrieve(staffRoleRequestId)
        if (!record) return
        reset({ ...record, ...defaultValues })
      } catch (error) {
        console.error("Failed to fetch staff role request:", error)
      } finally {
        setLoading(false)
      }
    }
    fetchRecord()
  }, [])

  const onSubmit: SubmitHandler<AdminStaffRoleRequestUpdateRequest> = async (data) => {
    setLoading(true)
    setApiErrors([])
    try {
      await update(data)
      showToast("Staff Role Request successfully updated", "success")
      onUpdated?.(staffRoleRequestId)
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

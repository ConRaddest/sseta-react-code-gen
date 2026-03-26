"use client"

import { useState, useEffect } from "react"
import { SubmitHandler, useForm } from "react-hook-form"
import { Button, FormTemplate, FormValidationErrors, FilterBy, OrderBy, extractApiErrors } from "@sseta/components"
import { useEcdSystemUser } from "@/contexts/resources/ecd/EcdSystemUserContext"
import { useToast } from "@/contexts/general/ToastContext"
import { EcdSystemUserUpdateRequest } from "@/types/api.types"
import useEcdSystemUserUpdateFields from "./useEcdSystemUserUpdateFields"
import EcdSystemUserUpdateLayout from "./EcdSystemUserUpdateLayout"

interface EcdSystemUserUpdateFormProps {
  systemUserId: number
  defaultValues?: Partial<EcdSystemUserUpdateRequest>
  disabledFields?: string[]
  hiddenFields?: string[]
  selectFilterBys?: Record<string, FilterBy[]>
  selectOrderBys?: Record<string, OrderBy[]>
  renderActionsInFooter?: boolean
  className?: string
  loading?: boolean
  onUpdated?: (systemUserId: number) => void
}

export default function EcdSystemUserUpdateForm(props: EcdSystemUserUpdateFormProps) {
  const {
    systemUserId,
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

  const { retrieve, update } = useEcdSystemUser()
  const { showToast } = useToast()

  const {
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<EcdSystemUserUpdateRequest>({
    mode: "onBlur",
  })

  const fields = useEcdSystemUserUpdateFields({ errors, disabledFields, selectFilterBys, selectOrderBys })

  useEffect(() => {
    const fetchRecord = async () => {
      setLoading(true)
      try {
        const record = await retrieve(systemUserId)
        if (!record) return
        reset({ ...record, ...defaultValues })
      } catch (error) {
        console.error("Failed to fetch system user:", error)
      } finally {
        setLoading(false)
      }
    }
    fetchRecord()
  }, [])

  const onSubmit: SubmitHandler<EcdSystemUserUpdateRequest> = async (data) => {
    setLoading(true)
    setApiErrors([])
    try {
      await update(data)
      showToast("System User successfully updated", "success")
      onUpdated?.(systemUserId)
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
        layout={EcdSystemUserUpdateLayout}
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
